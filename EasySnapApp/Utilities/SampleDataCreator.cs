using System;
using System.IO;
using EasySnapApp.Models;
using EasySnapApp.Repositories;

namespace EasySnapApp.Utilities
{
    public static class SampleDataCreator
    {
        public static async void CreateSampleData()
        {
            try
            {
                var repository = new SQLiteImageRepository();
                
                // Check if data already exists
                var existingImages = await repository.GetAllImagesAsync();
                if (existingImages.Count > 0)
                    return; // Data already exists, skip creation
                
                // Create some sample images
                var images = new[]
                {
                    new ImageRecord
                    {
                        PartNumber = "PART001",
                        Sequence = 1,
                        FullPath = @"C:\SampleImages\PART001_001.jpg",
                        FileName = "PART001_001.jpg",
                        CaptureTimeUtc = DateTime.UtcNow.AddHours(-2),
                        FileSizeBytes = 2048000,
                        Weight = 125.5,
                        DimX = 10.5,
                        DimY = 8.2,
                        DimZ = 3.1,
                        Metadata = "Sample metadata for first image"
                    },
                    new ImageRecord
                    {
                        PartNumber = "PART001",
                        Sequence = 2,
                        FullPath = @"C:\SampleImages\PART001_002.jpg",
                        FileName = "PART001_002.jpg",
                        CaptureTimeUtc = DateTime.UtcNow.AddHours(-1),
                        FileSizeBytes = 1945000,
                        Weight = 125.5,
                        DimX = 10.5,
                        DimY = 8.2,
                        DimZ = 3.1,
                        Metadata = "Sample metadata for second image"
                    },
                    new ImageRecord
                    {
                        PartNumber = "PART002",
                        Sequence = 1,
                        FullPath = @"C:\SampleImages\PART002_001.jpg",
                        FileName = "PART002_001.jpg",
                        CaptureTimeUtc = DateTime.UtcNow.AddMinutes(-30),
                        FileSizeBytes = 2156000,
                        Weight = 98.3,
                        DimX = 15.2,
                        DimY = 12.1,
                        DimZ = 4.5,
                        Metadata = "Sample metadata for part 2"
                    }
                };

                foreach (var image in images)
                {
                    try
                    {
                        await repository.AddImageAsync(image);
                    }
                    catch (Exception ex)
                    {
                        // Ignore duplicates
                        System.Diagnostics.Debug.WriteLine($"Could not add sample image: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sample data creation error: {ex.Message}");
            }
        }
        
        public static void CreateSampleImageFiles()
        {
            var sampleDir = @"C:\SampleImages";
            if (!Directory.Exists(sampleDir))
            {
                Directory.CreateDirectory(sampleDir);
            }
            
            // Create simple test image files if they don't exist
            var imageFiles = new[]
            {
                "PART001_001.jpg",
                "PART001_002.jpg", 
                "PART002_001.jpg"
            };
            
            foreach (var fileName in imageFiles)
            {
                var filePath = Path.Combine(sampleDir, fileName);
                if (!File.Exists(filePath))
                {
                    try
                    {
                        // Create a simple 100x100 test image
                        using (var bitmap = new System.Drawing.Bitmap(100, 100))
                        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(System.Drawing.Color.LightBlue);
                            graphics.DrawString(Path.GetFileNameWithoutExtension(fileName), 
                                new System.Drawing.Font("Arial", 8), 
                                System.Drawing.Brushes.Black, 10, 10);
                            
                            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not create sample image {fileName}: {ex.Message}");
                    }
                }
            }
        }
    }
}
