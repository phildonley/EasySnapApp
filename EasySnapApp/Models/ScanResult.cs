using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace EasySnapApp.Models
{
    public class ScanResult : INotifyPropertyChanged
    {
        private string _partNumber;
        private int _sequence;
        private string _imageFileName;
        private string _timeStamp;
        private double _lengthIn;
        private double _depthIn;
        private double _heightIn;
        private double _weightLb;
        private BitmapImage _thumbnailImage;
        private string _tooltipText;
        private bool _isSelected;

        public string PartNumber
        {
            get => _partNumber;
            set { _partNumber = value; OnPropertyChanged(); }
        }

        public int Sequence
        {
            get => _sequence;
            set { _sequence = value; OnPropertyChanged(); }
        }

        public string ImageFileName
        {
            get => _imageFileName;
            set { _imageFileName = value; OnPropertyChanged(); }
        }

        public string TimeStamp
        {
            get => _timeStamp;
            set { _timeStamp = value; OnPropertyChanged(); }
        }

        public double LengthIn
        {
            get => _lengthIn;
            set { _lengthIn = value; OnPropertyChanged(); OnPropertyChanged(nameof(Dims)); }
        }

        // NOTE: DepthIn is being used as "Width" in the UI/export right now.
        public double DepthIn
        {
            get => _depthIn;
            set { _depthIn = value; OnPropertyChanged(); OnPropertyChanged(nameof(Dims)); }
        }

        public double HeightIn
        {
            get => _heightIn;
            set { _heightIn = value; OnPropertyChanged(); OnPropertyChanged(nameof(Dims)); }
        }

        public double WeightLb
        {
            get => _weightLb;
            set { _weightLb = value; OnPropertyChanged(); }
        }

        public BitmapImage ThumbnailImage
        {
            get => _thumbnailImage;
            set { _thumbnailImage = value; OnPropertyChanged(); }
        }

        public string TooltipText
        {
            get => _tooltipText;
            set { _tooltipText = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // Convenience property for display, e.g. "5.12×3.50×1.80"
        public string Dims => $"{LengthIn:F2}×{DepthIn:F2}×{HeightIn:F2}";

        public ScanResult()
        {
            _partNumber = "";
            _imageFileName = "";
            _timeStamp = "";
            _thumbnailImage = new BitmapImage();
            _tooltipText = "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}