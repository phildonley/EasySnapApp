using System;
using System.ComponentModel;

namespace EasySnapApp.Models
{
    public class ImageRecord : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public int Id { get; set; }
        public string PartNumber { get; set; }
        public int Sequence { get; set; }
        public string FullPath { get; set; }
        public string FileName { get; set; }
        public DateTime CaptureTimeUtc { get; set; }
        public long FileSizeBytes { get; set; }
        public double? Weight { get; set; }
        public double? DimX { get; set; }
        public double? DimY { get; set; }
        public double? DimZ { get; set; }
        public string Metadata { get; set; }
        
        public bool IsSelected 
        { 
            get { return _isSelected; }
            set 
            { 
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        
        public string DisplayName 
        { 
            get 
            { 
                return $"{PartNumber}_{Sequence:D3}"; 
            } 
        }
        
        public string FileSizeMB 
        { 
            get 
            { 
                return $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB"; 
            } 
        }
        
        public string DimensionsDisplay 
        { 
            get 
            { 
                if (DimX.HasValue && DimY.HasValue && DimZ.HasValue)
                    return $"{DimX:F2} x {DimY:F2} x {DimZ:F2}";
                return "N/A";
            } 
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
