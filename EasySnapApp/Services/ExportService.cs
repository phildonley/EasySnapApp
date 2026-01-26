using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasySnapApp.Models;

namespace EasySnapApp.Services
{
    public class ExportService
    {
        public async Task<bool> ExportAsync(
            List<ImageRecord> selectedImages, 
            ExportOptions options, 
            IProgress<ExportProgressEventArgs> progress = null, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (selectedImages == null || !selectedImages.Any())
                    throw new ArgumentException("No images selected for export");

                if (string.IsNullOrWhiteSpace(options.OutputFolder))
                    throw new ArgumentException("Output folder must be specified");

                if (!Directory.Exists(options.OutputFolder))
                    Directory.CreateDirectory(options.OutputFolder);

                var exportPaths = new List<(ImageRecord image, string exportPath)>();
                var totalImages = selectedImages.Count;

                // Process each image
                for (int i = 0; i < selectedImages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var image = selectedImages[i];
                    progress?.Report(new ExportProgressEventArgs
                    {
                        CurrentImage = i + 1,
                        TotalImages = totalImages,
                        CurrentImageName = image.FileName,
                        Message = $"Processing {image.FileName}..."
                    });

                    try
                    {
                        string exportPath = await ProcessImageAsync(image, options, cancellationToken);
                        if (!string.IsNullOrEmpty(exportPath))
                        {
                            exportPaths.Add((image, exportPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report(new ExportProgressEventArgs
                        {
                            CurrentImage = i + 1,
                            TotalImages = totalImages,
                            CurrentImageName = image.FileName,
                            Message = $"Error processing {image.FileName}: {ex.Message}",
                            HasError = true
                        });
                        
                        // Continue processing other images rather than failing completely
                    }
                }

                // Create manifest if requested
                if (options.IncludeManifest && exportPaths.Any())
                {
                    progress?.Report(new ExportProgressEventArgs
                    {
                        CurrentImage = totalImages,
                        TotalImages = totalImages,
                        Message = "Creating manifest..."
                    });

                    await CreateManifestAsync(exportPaths, options);
                }

                // Create ZIP if requested
                if (options.CreateZip && exportPaths.Any())
                {
                    progress?.Report(new ExportProgressEventArgs
                    {
                        CurrentImage = totalImages,
                        TotalImages = totalImages,
                        Message = "Creating ZIP archive..."
                    });

                    await CreateZipArchiveAsync(exportPaths, options);
                }

                progress?.Report(new ExportProgressEventArgs
                {
                    CurrentImage = totalImages,
                    TotalImages = totalImages,
                    Message = $"Export completed. {exportPaths.Count} images processed successfully.",
                    IsCompleted = true
                });

                return exportPaths.Any();
            }
            catch (Exception ex)
            {
                progress?.Report(new ExportProgressEventArgs
                {
                    Message = $"Export failed: {ex.Message}",
                    HasError = true
                });
                throw;
            }
        }

        private async Task<string> ProcessImageAsync(ImageRecord image, ExportOptions options, CancellationToken cancellationToken)
        {
            if (!File.Exists(image.FullPath))
                return null;

            string outputFileName = Path.GetFileNameWithoutExtension(image.FileName) + ".jpg";
            string outputPath = Path.Combine(options.OutputFolder, outputFileName);

            if (options.SizeMode == ExportSizeMode.Original)
            {
                // Just copy the file if it's already a JPEG and we want original size
                if (Path.GetExtension(image.FullPath).ToLowerInvariant() == ".jpg")
                {
                    await Task.Run(() => File.Copy(image.FullPath, outputPath, true), cancellationToken);
                    return outputPath;
                }
            }

            // Load and process the image
            await Task.Run(() => 
            {
                using (var originalImage = Image.FromFile(image.FullPath))
                {
                    using (var processedImage = ProcessImageForExport(originalImage, options))
                    {
                        var encoder = GetJpegEncoder();
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)options.JpegQuality);
                        
                        processedImage.Save(outputPath, encoder, encoderParams);
                    }
                }
            }, cancellationToken);

            return outputPath;
        }

        private Image ProcessImageForExport(Image originalImage, ExportOptions options)
        {
            if (options.SizeMode == ExportSizeMode.Original)
            {
                return new Bitmap(originalImage);
            }

            Size newSize = CalculateNewSize(originalImage.Size, options);
            
            var bitmap = new Bitmap(newSize.Width, newSize.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                graphics.Clear(Color.White);
                graphics.DrawImage(originalImage, 0, 0, newSize.Width, newSize.Height);
            }

            return bitmap;
        }

        private Size CalculateNewSize(Size originalSize, ExportOptions options)
        {
            switch (options.SizeMode)
            {
                case ExportSizeMode.LongEdge:
                    {
                        var longEdge = Math.Max(originalSize.Width, originalSize.Height);
                        if (longEdge <= options.LongEdgePixels)
                            return originalSize;

                        var ratio = (double)options.LongEdgePixels / longEdge;
                        return new Size(
                            (int)(originalSize.Width * ratio),
                            (int)(originalSize.Height * ratio));
                    }

                case ExportSizeMode.FitInside:
                    {
                        var widthRatio = (double)options.FitWidth / originalSize.Width;
                        var heightRatio = (double)options.FitHeight / originalSize.Height;
                        var ratio = Math.Min(widthRatio, heightRatio);
                        
                        if (ratio >= 1.0)
                            return originalSize;

                        return new Size(
                            (int)(originalSize.Width * ratio),
                            (int)(originalSize.Height * ratio));
                    }

                default:
                    return originalSize;
            }
        }

        private ImageCodecInfo GetJpegEncoder()
        {
            var encoders = ImageCodecInfo.GetImageEncoders();
            return encoders.FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);
        }

        private async Task CreateManifestAsync(List<(ImageRecord image, string exportPath)> exportPaths, ExportOptions options)
        {
            var manifestPath = Path.Combine(options.OutputFolder, "manifest.csv");
            var csv = new StringBuilder();
            
            // CSV Header
            csv.AppendLine("PartNumber,Sequence,SourcePath,ExportPath,CaptureTimeUtc,FileSizeBytes,Weight,DimX,DimY,DimZ");
            
            foreach (var (image, exportPath) in exportPaths)
            {
                var fileInfo = new FileInfo(exportPath);
                csv.AppendLine($"\"{image.PartNumber}\",{image.Sequence},\"{image.FullPath}\",\"{exportPath}\"," +
                              $"{image.CaptureTimeUtc:yyyy-MM-dd HH:mm:ss},{fileInfo.Length}," +
                              $"{image.Weight?.ToString() ?? ""},{image.DimX?.ToString() ?? ""}," +
                              $"{image.DimY?.ToString() ?? ""},{image.DimZ?.ToString() ?? ""}");
            }

            await Task.Run(() => File.WriteAllText(manifestPath, csv.ToString()));
        }

        private async Task CreateZipArchiveAsync(List<(ImageRecord image, string exportPath)> exportPaths, ExportOptions options)
        {
            var zipPath = Path.Combine(options.OutputFolder, "export.zip");
            
            using (var fileStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                foreach (var (image, exportPath) in exportPaths)
                {
                    if (File.Exists(exportPath))
                    {
                        var entryName = Path.GetFileName(exportPath);
                        var entry = archive.CreateEntry(entryName);
                        
                        using (var entryStream = entry.Open())
                        using (var sourceFileStream = new FileStream(exportPath, FileMode.Open, FileAccess.Read))
                        {
                            await sourceFileStream.CopyToAsync(entryStream);
                        }
                    }
                }

                // Include manifest if it exists
                var manifestPath = Path.Combine(options.OutputFolder, "manifest.csv");
                if (File.Exists(manifestPath))
                {
                    var manifestEntry = archive.CreateEntry("manifest.csv");
                    using (var entryStream = manifestEntry.Open())
                    using (var manifestFileStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read))
                    {
                        await manifestFileStream.CopyToAsync(entryStream);
                    }
                }
            }
        }
    }

    public class ExportProgressEventArgs : EventArgs
    {
        public int CurrentImage { get; set; }
        public int TotalImages { get; set; }
        public string CurrentImageName { get; set; }
        public string Message { get; set; }
        public bool HasError { get; set; }
        public bool IsCompleted { get; set; }

        public double PercentComplete 
        { 
            get 
            { 
                if (TotalImages == 0) return 0;
                return (double)CurrentImage / TotalImages * 100; 
            } 
        }
    }
}
