using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using EasySnapApp.Data;

namespace EasySnapApp.Views
{
    public partial class DimsExportSelectionWindow : Window
    {
        public List<CapturedImage> SelectedImages { get; private set; }
        
        public DimsExportSelectionWindow(List<CapturedImage> availableImages)
        {
            InitializeComponent();
            
            // Group by part number with counts
            var partGroups = availableImages
                .GroupBy(i => i.PartNumber)
                .Select(g => new PartGroupInfo 
                {
                    PartNumber = g.Key,
                    ImageCount = g.Count(),
                    DateRange = $"{g.Min(i => i.CaptureTimeUtc):MM/dd} - {g.Max(i => i.CaptureTimeUtc):MM/dd}",
                    IsSelected = true,
                    Images = g.ToList()
                }).ToList();
                
            dgPartSelection.ItemsSource = partGroups;
            UpdateSummary();
        }
        
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var selectedParts = dgPartSelection.Items.Cast<PartGroupInfo>().Where(p => p.IsSelected);
            SelectedImages = selectedParts.SelectMany(p => p.Images).ToList();
            
            if (!SelectedImages.Any())
            {
                MessageBox.Show("Please select at least one part to export.", "No Selection");
                return;
            }
            
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (PartGroupInfo part in dgPartSelection.Items)
                part.IsSelected = true;
            dgPartSelection.Items.Refresh();
            UpdateSummary();
        }
        
        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (PartGroupInfo part in dgPartSelection.Items)
                part.IsSelected = false;
            dgPartSelection.Items.Refresh();
            UpdateSummary();
        }
        
        private void UpdateSummary()
        {
            var selected = dgPartSelection.Items.Cast<PartGroupInfo>().Where(p => p.IsSelected);
            txtSummary.Text = $"Selected: {selected.Sum(p => p.ImageCount)} images from {selected.Count()} parts";
        }
    }
    
    public class PartGroupInfo
    {
        public string PartNumber { get; set; }
        public int ImageCount { get; set; }
        public string DateRange { get; set; }
        public bool IsSelected { get; set; }
        public List<CapturedImage> Images { get; set; }
    }
}
