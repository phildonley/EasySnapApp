using System;
using System.IO;
using System.Windows;

namespace EasySnapApp.Services
{
    public class CanonCameraService
    {
        /// <summary>
        /// Always true here (placeholder). Later, add real USB/SDK‐detection logic.
        /// </summary>
        public bool IsConnected => true;

        private readonly string _saveDir;
        private int _seq = 103; // Start sequence for image files

        public CanonCameraService(string saveDirectory)
        {
            _saveDir = saveDirectory;
            Directory.CreateDirectory(_saveDir);
        }

        /// <summary>
        /// Grabs (or fakes) an image, writes it to disk, returns the full path.
        /// </summary>
        public string CaptureImage(string partNumber)
        {
            string filename = $"{partNumber}.{_seq:000}.jpg";
            string fullPath = Path.Combine(_saveDir, filename);

            // Use placeholder image from Assets for now
            var uri = new Uri("pack://application:,,,/Assets/placeholder.jpg", UriKind.Absolute);
            var resource = Application.GetResourceStream(uri);
            if (resource == null) return null;

            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            resource.Stream.CopyTo(fs);

            _seq += 2; // Increment by 2 (legacy file convention?)
            return fullPath;
        }
    }
}
