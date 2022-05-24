using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using JC3FOVFixer.Annotations;
using JC3FOVFixer.Utilites;
using System.Windows.Input;

namespace JC3FOVFixer
{
    public class Model : INotifyPropertyChanged
    {
        private const float DegToRad = (float) (Math.PI/180.0f);
        private const float RadToDeg = (float) (180.0f/Math.PI);

        private readonly IntPtr _handle;
        private readonly float _defaultFOV = 34.89f;
        private readonly byte[] _originalCallBytes;

        private bool _fovHackEnabled;
        private float _fovRecall;
        private ICommand _restoreDefaultFOVCommand;

        public Model()
        {
            var processes = Process.GetProcessesByName("JustCause3");

            if (processes.Length == 0)
            {
                MessageBox.Show("Failed to find JustCause3.exe process! Exiting...", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(0);

                return;
            }

            _handle = Natives.OpenProcess((uint) Natives.ProcessAccessFlags.VMOperation | (uint) Natives.ProcessAccessFlags.VMRead | (uint) Natives.ProcessAccessFlags.VMWrite, false, processes[0].Id);

            if (_handle == IntPtr.Zero)
            {
                MessageBox.Show("Failed to open a handle to JustCause3.exe process! Exiting...", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(0);
            }

            _originalCallBytes = Natives.ReadBytes(_handle, _setFovCall, 5);

            PatchSetFov(true);
        }

        #region Properties

        public float Fov
        {
            get
            {
                return GetFov()*RadToDeg;
            }
            set
            {
                PatchFov((float)Math.Round(value) * DegToRad);

                OnPropertyChanged();
            }
        }

        public ICommand RestoreDefaultFOV
        {
            get
            {
                return _restoreDefaultFOVCommand ?? (_restoreDefaultFOVCommand = new RelayCommand(p => Fov = _defaultFOV));
            }
        }

        public bool FovHackEnabled
        {
            get
            {
                return _fovHackEnabled;
            }
        }

        #endregion

        ~Model()
        {
            // Only reset FOV if hack was enabled
            if (_fovHackEnabled)
            {
                PatchSetFov(false);
            }
        }

        public void PatchSetFov(bool overrideEnable)
        {
            _fovHackEnabled = overrideEnable;

            Natives.WriteBytes(_handle, _setFovCall, overrideEnable ? new byte[] {0x90, 0x90, 0x90, 0x90, 0x90} : _originalCallBytes);
        }

        public void RecallFov()
        {
            PatchFov(_fovRecall);
        }

        private void PatchFov(float newFov)
        {
            _fovRecall = newFov;

            var cameraManager = Natives.ReadIntPtr(_handle, _cameraManagerPtr);
            var currentCamera = Natives.ReadIntPtr(_handle, cameraManager + CurrentCameraOffset);

            // Update the flags to indicate an FOV change has occurred
            var flags = Natives.ReadBytes(_handle, currentCamera + CameraFlagsOffset, 1);
            flags[0] |= 0x10;
            Natives.WriteBytes(_handle, currentCamera + CameraFlagsOffset, flags);

            // Update the actual FOV values
            Natives.WriteFloat(_handle, currentCamera + FovOffset1, newFov);
            Natives.WriteFloat(_handle, currentCamera + FovOffset2, newFov);
        }

        private float GetFov()
        {
            var cameraManager = Natives.ReadIntPtr(_handle, _cameraManagerPtr);
            var currentCamera = Natives.ReadIntPtr(_handle, cameraManager + CurrentCameraOffset);

            return Natives.ReadFloat(_handle, currentCamera + FovOffset2);
        }

        #region Offsets

        // Patch version 1.05 (29/07/2016)
        private readonly IntPtr _cameraManagerPtr = new IntPtr(0x142ED0E20);
        private readonly IntPtr _setFovCall = new IntPtr(0x143AEFF41);

        private const int CurrentCameraOffset = 0x5c0;
        private const int CameraFlagsOffset = 0x55e;
        private const int FovOffset1 = 0x580;
        private const int FovOffset2 = 0x584;

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}