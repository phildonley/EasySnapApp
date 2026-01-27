using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace EasySnapApp.Data
{
    /// <summary>
    /// Data models for EasySnap database
    /// </summary>
    public class CaptureSession
    {
        public string SessionId { get; set; }
        public string PartNumber { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime? EndTimeUtc { get; set; }
        public bool IsActive { get; set; }
    }

    public class CapturedImage
    {
        public string ImageId { get; set; }
        public string SessionId { get; set; }
        public string PartNumber { get; set; }
        public int Sequence { get; set; }
        public string FullPath { get; set; }
        public string ThumbPath { get; set; }
        public DateTime CaptureTimeUtc { get; set; }
        public long FileSizeBytes { get; set; }
        public int? WidthPx { get; set; }
        public int? HeightPx { get; set; }
        public double? WeightGrams { get; set; }
        public double? DimX { get; set; }
        public double? DimY { get; set; }
        public double? DimZ { get; set; }
        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// Repository for capture data CRUD operations
    /// Phase 2: Single-event, clean database operations
    /// </summary>
    public class CaptureRepository
    {
        private readonly EasySnapDb _database;

        public CaptureRepository(EasySnapDb database)
        {
            _database = database;
        }

        #region Session Management

        /// <summary>
        /// Get or create an active session for a part number
        /// </summary>
        public CaptureSession GetOrCreateActiveSession(string partNumber)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                // First try to find existing active session
                var selectSql = @"
                    SELECT SessionId, PartNumber, StartTimeUtc, EndTimeUtc, IsActive 
                    FROM CaptureSessions 
                    WHERE PartNumber = @partNumber AND IsActive = 1 
                    LIMIT 1";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CaptureSession
                            {
                                SessionId = reader.GetString(0), // SessionId
                                PartNumber = reader.GetString(1), // PartNumber
                                StartTimeUtc = DateTime.Parse(reader.GetString(2)), // StartTimeUtc
                                EndTimeUtc = reader.IsDBNull(3) ? null : (DateTime?)DateTime.Parse(reader.GetString(3)), // EndTimeUtc
                                IsActive = reader.GetInt32(4) == 1 // IsActive
                            };
                        }
                    }
                }

                // Create new session if none found
                var newSession = new CaptureSession
                {
                    SessionId = Guid.NewGuid().ToString(),
                    PartNumber = partNumber,
                    StartTimeUtc = DateTime.UtcNow,
                    IsActive = true
                };

                var insertSql = @"
                    INSERT INTO CaptureSessions (SessionId, PartNumber, StartTimeUtc, IsActive)
                    VALUES (@sessionId, @partNumber, @startTime, 1)";

                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", newSession.SessionId);
                    command.Parameters.AddWithValue("@partNumber", newSession.PartNumber);
                    command.Parameters.AddWithValue("@startTime", newSession.StartTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.ExecuteNonQuery();
                }

                return newSession;
            }
        }

        /// <summary>
        /// Get the most recent session (for app startup)
        /// </summary>
        public CaptureSession GetMostRecentSession()
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT SessionId, PartNumber, StartTimeUtc, EndTimeUtc, IsActive 
                    FROM CaptureSessions 
                    ORDER BY StartTimeUtc DESC 
                    LIMIT 1";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CaptureSession
                            {
                                SessionId = reader.GetString(0), // SessionId
                                PartNumber = reader.GetString(1), // PartNumber
                                StartTimeUtc = DateTime.Parse(reader.GetString(2)), // StartTimeUtc
                                EndTimeUtc = reader.IsDBNull(3) ? null : (DateTime?)DateTime.Parse(reader.GetString(3)), // EndTimeUtc
                                IsActive = reader.GetInt32(4) == 1 // IsActive
                            };
                        }
                    }
                }
            }

            return null;
        }

        #endregion

        #region Image Management

        /// <summary>
        /// Insert a new captured image record
        /// </summary>
        public void InsertCapturedImage(CapturedImage image)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var insertSql = @"
                    INSERT INTO CapturedImages (
                        ImageId, SessionId, PartNumber, Sequence, FullPath, ThumbPath,
                        CaptureTimeUtc, FileSizeBytes, WidthPx, HeightPx, WeightGrams,
                        DimX, DimY, DimZ, IsDeleted
                    ) VALUES (
                        @imageId, @sessionId, @partNumber, @sequence, @fullPath, @thumbPath,
                        @captureTime, @fileSize, @widthPx, @heightPx, @weightGrams,
                        @dimX, @dimY, @dimZ, @isDeleted
                    )";

                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@imageId", image.ImageId);
                    command.Parameters.AddWithValue("@sessionId", image.SessionId);
                    command.Parameters.AddWithValue("@partNumber", image.PartNumber);
                    command.Parameters.AddWithValue("@sequence", image.Sequence);
                    command.Parameters.AddWithValue("@fullPath", image.FullPath);
                    command.Parameters.AddWithValue("@thumbPath", image.ThumbPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@captureTime", image.CaptureTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@fileSize", image.FileSizeBytes);
                    command.Parameters.AddWithValue("@widthPx", image.WidthPx ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@heightPx", image.HeightPx ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@weightGrams", image.WeightGrams ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dimX", image.DimX ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dimY", image.DimY ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dimZ", image.DimZ ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@isDeleted", image.IsDeleted ? 1 : 0);

                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Get all images for a part number, ordered by sequence (newest first)
        /// </summary>
        public List<CapturedImage> GetImagesForPart(string partNumber)
        {
            var images = new List<CapturedImage>();

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT ImageId, SessionId, PartNumber, Sequence, FullPath, ThumbPath,
                           CaptureTimeUtc, FileSizeBytes, WidthPx, HeightPx, WeightGrams,
                           DimX, DimY, DimZ, IsDeleted
                    FROM CapturedImages 
                    WHERE IsDeleted = 0 AND PartNumber = @partNumber
                    ORDER BY Sequence DESC";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            images.Add(new CapturedImage
                            {
                                ImageId = reader.GetString(0), // ImageId
                                SessionId = reader.GetString(1), // SessionId
                                PartNumber = reader.GetString(2), // PartNumber
                                Sequence = reader.GetInt32(3), // Sequence
                                FullPath = reader.GetString(4), // FullPath
                                ThumbPath = reader.IsDBNull(5) ? null : reader.GetString(5), // ThumbPath
                                CaptureTimeUtc = DateTime.Parse(reader.GetString(6)), // CaptureTimeUtc
                                FileSizeBytes = reader.GetInt64(7), // FileSizeBytes
                                WidthPx = reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8), // WidthPx
                                HeightPx = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9), // HeightPx
                                WeightGrams = reader.IsDBNull(10) ? null : (double?)reader.GetDouble(10), // WeightGrams
                                DimX = reader.IsDBNull(11) ? null : (double?)reader.GetDouble(11), // DimX
                                DimY = reader.IsDBNull(12) ? null : (double?)reader.GetDouble(12), // DimY
                                DimZ = reader.IsDBNull(13) ? null : (double?)reader.GetDouble(13), // DimZ
                                IsDeleted = reader.GetInt32(14) == 1 // IsDeleted
                            });
                        }
                    }
                }
            }

            return images;
        }

        /// <summary>
        /// Get the next sequence number for a part (Phase 3.9: Gap reuse)
        /// Returns smallest available sequence ≥ 103
        /// </summary>
        public int GetNextSequenceForPart(string partNumber)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                // Get all existing sequences for this part
                var selectSql = @"
                    SELECT Sequence 
                    FROM CapturedImages 
                    WHERE PartNumber = @partNumber AND IsDeleted = 0
                    ORDER BY Sequence ASC";

                var existingSequences = new List<int>();
                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            existingSequences.Add(reader.GetInt32(0));
                        }
                    }
                }

                // Find smallest gap starting from 103
                int nextSequence = 103;
                foreach (var seq in existingSequences)
                {
                    if (seq == nextSequence)
                    {
                        nextSequence++;
                    }
                    else if (seq > nextSequence)
                    {
                        break; // Found a gap
                    }
                }

                return nextSequence;
            }
        }

        /// <summary>
        /// Update thumbnail path for an existing image
        /// </summary>
        public void UpdateImageThumbnail(string imageId, string thumbPath)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var updateSql = "UPDATE CapturedImages SET ThumbPath = @thumbPath WHERE ImageId = @imageId";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@thumbPath", thumbPath);
                    command.Parameters.AddWithValue("@imageId", imageId);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Mark image as deleted (soft delete)
        /// </summary>
        public void MarkImageDeleted(string imageId)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var updateSql = "UPDATE CapturedImages SET IsDeleted = 1 WHERE ImageId = @imageId";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@imageId", imageId);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Get all captured images from all parts (for export window)
        /// </summary>
        public List<CapturedImage> GetAllImages()
        {
            var images = new List<CapturedImage>();

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT ImageId, SessionId, PartNumber, Sequence, FullPath, ThumbPath,
                           CaptureTimeUtc, FileSizeBytes, WidthPx, HeightPx, WeightGrams,
                           DimX, DimY, DimZ, IsDeleted
                    FROM CapturedImages 
                    WHERE IsDeleted = 0
                    ORDER BY PartNumber ASC, Sequence ASC";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            images.Add(new CapturedImage
                            {
                                ImageId = reader.GetString(0), // ImageId
                                SessionId = reader.GetString(1), // SessionId
                                PartNumber = reader.GetString(2), // PartNumber
                                Sequence = reader.GetInt32(3), // Sequence
                                FullPath = reader.GetString(4), // FullPath
                                ThumbPath = reader.IsDBNull(5) ? null : reader.GetString(5), // ThumbPath
                                CaptureTimeUtc = DateTime.Parse(reader.GetString(6)), // CaptureTimeUtc
                                FileSizeBytes = reader.GetInt64(7), // FileSizeBytes
                                WidthPx = reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8), // WidthPx
                                HeightPx = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9), // HeightPx
                                WeightGrams = reader.IsDBNull(10) ? null : (double?)reader.GetDouble(10), // WeightGrams
                                DimX = reader.IsDBNull(11) ? null : (double?)reader.GetDouble(11), // DimX
                                DimY = reader.IsDBNull(12) ? null : (double?)reader.GetDouble(12), // DimY
                                DimZ = reader.IsDBNull(13) ? null : (double?)reader.GetDouble(13), // DimZ
                                IsDeleted = reader.GetInt32(14) == 1 // IsDeleted
                            });
                        }
                    }
                }
            }

            return images;
        }

        /// <summary>
        /// Get distinct part numbers from captured images (for export window filter)
        /// </summary>
        public List<string> GetDistinctPartNumbers()
        {
            var partNumbers = new List<string>();

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT DISTINCT PartNumber 
                    FROM CapturedImages 
                    WHERE IsDeleted = 0
                    ORDER BY PartNumber ASC";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            partNumbers.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return partNumbers;
        }

        /// <summary>
        /// Validate file paths and mark missing files as deleted
        /// </summary>
        public int CleanupMissingFiles(string partNumber)
        {
            var images = GetImagesForPart(partNumber);
            int cleanedCount = 0;

            foreach (var image in images)
            {
                if (!File.Exists(image.FullPath))
                {
                    MarkImageDeleted(image.ImageId);
                    cleanedCount++;
                }
            }

            return cleanedCount;
        }

        #endregion

        #region Phase 3.9: Enhanced Operations

        /// <summary>
        /// Get all images across ALL parts, ordered by capture time (newest first)
        /// Phase 3.9: Persistent gallery view
        /// </summary>
        public List<CapturedImage> GetAllImagesNewestFirst()
        {
            var images = new List<CapturedImage>();

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT ImageId, SessionId, PartNumber, Sequence, FullPath, ThumbPath,
                           CaptureTimeUtc, FileSizeBytes, WidthPx, HeightPx, WeightGrams,
                           DimX, DimY, DimZ, IsDeleted
                    FROM CapturedImages 
                    WHERE IsDeleted = 0
                    ORDER BY CaptureTimeUtc DESC";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            images.Add(new CapturedImage
                            {
                                ImageId = reader.GetString(0),
                                SessionId = reader.GetString(1),
                                PartNumber = reader.GetString(2),
                                Sequence = reader.GetInt32(3),
                                FullPath = reader.GetString(4),
                                ThumbPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CaptureTimeUtc = DateTime.Parse(reader.GetString(6)),
                                FileSizeBytes = reader.GetInt64(7),
                                WidthPx = reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                                HeightPx = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9),
                                WeightGrams = reader.IsDBNull(10) ? null : (double?)reader.GetDouble(10),
                                DimX = reader.IsDBNull(11) ? null : (double?)reader.GetDouble(11),
                                DimY = reader.IsDBNull(12) ? null : (double?)reader.GetDouble(12),
                                DimZ = reader.IsDBNull(13) ? null : (double?)reader.GetDouble(13),
                                IsDeleted = reader.GetInt32(14) == 1
                            });
                        }
                    }
                }
            }

            return images;
        }

        /// <summary>
        /// Phase 3.9: Delete multiple captures safely (DB + files)
        /// </summary>
        public void DeleteCaptures(IEnumerable<string> imageIds, Action<string> logger = null)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var imageId in imageIds)
                        {
                            // Get file paths before deleting from DB
                            var selectSql = "SELECT FullPath, ThumbPath FROM CapturedImages WHERE ImageId = @imageId";
                            string fullPath = null, thumbPath = null;

                            using (var selectCmd = new SQLiteCommand(selectSql, connection, transaction))
                            {
                                selectCmd.Parameters.AddWithValue("@imageId", imageId);
                                using (var reader = selectCmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        fullPath = reader.GetString(0);
                                        thumbPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                                    }
                                }
                            }

                            // Mark as deleted in database
                            var deleteSql = "UPDATE CapturedImages SET IsDeleted = 1 WHERE ImageId = @imageId";
                            using (var deleteCmd = new SQLiteCommand(deleteSql, connection, transaction))
                            {
                                deleteCmd.Parameters.AddWithValue("@imageId", imageId);
                                deleteCmd.ExecuteNonQuery();
                            }

                            // Delete physical files
                            try
                            {
                                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                                {
                                    File.Delete(fullPath);
                                    logger?.Invoke($"Deleted file: {Path.GetFileName(fullPath)}");
                                }
                                if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                                {
                                    File.Delete(thumbPath);
                                    logger?.Invoke($"Deleted thumbnail: {Path.GetFileName(thumbPath)}");
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.Invoke($"File delete error for {imageId}: {ex.Message}");
                                // Continue with other deletions
                            }
                        }

                        transaction.Commit();
                        logger?.Invoke($"Successfully deleted {imageIds.Count()} captures");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        logger?.Invoke($"Delete operation failed: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Phase 3.9: Update image sequence and filename (for resequencing)
        /// </summary>
        public void UpdateImageSequence(string imageId, int newSequence, string newFullPath, string newThumbPath = null)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var updateSql = @"
                    UPDATE CapturedImages 
                    SET Sequence = @sequence, FullPath = @fullPath, ThumbPath = @thumbPath
                    WHERE ImageId = @imageId";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@sequence", newSequence);
                    command.Parameters.AddWithValue("@fullPath", newFullPath);
                    command.Parameters.AddWithValue("@thumbPath", newThumbPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@imageId", imageId);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Phase 3.9: Get image by ImageId
        /// </summary>
        public CapturedImage GetImageById(string imageId)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT ImageId, SessionId, PartNumber, Sequence, FullPath, ThumbPath,
                           CaptureTimeUtc, FileSizeBytes, WidthPx, HeightPx, WeightGrams,
                           DimX, DimY, DimZ, IsDeleted
                    FROM CapturedImages 
                    WHERE ImageId = @imageId";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@imageId", imageId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CapturedImage
                            {
                                ImageId = reader.GetString(0),
                                SessionId = reader.GetString(1),
                                PartNumber = reader.GetString(2),
                                Sequence = reader.GetInt32(3),
                                FullPath = reader.GetString(4),
                                ThumbPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CaptureTimeUtc = DateTime.Parse(reader.GetString(6)),
                                FileSizeBytes = reader.GetInt64(7),
                                WidthPx = reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                                HeightPx = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9),
                                WeightGrams = reader.IsDBNull(10) ? null : (double?)reader.GetDouble(10),
                                DimX = reader.IsDBNull(11) ? null : (double?)reader.GetDouble(11),
                                DimY = reader.IsDBNull(12) ? null : (double?)reader.GetDouble(12),
                                DimZ = reader.IsDBNull(13) ? null : (double?)reader.GetDouble(13),
                                IsDeleted = reader.GetInt32(14) == 1
                            };
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 3.9: Resequence all images in a part contiguously starting from 103
        /// Safe rename with temporary files to avoid collisions
        /// </summary>
        public void ResequencePart(string partNumber, List<string> orderedImageIds, Action<string> logger = null)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        logger?.Invoke($"Starting resequence for part {partNumber} with {orderedImageIds.Count} images");

                        var tempFileMap = new Dictionary<string, (string tempFull, string tempThumb, string finalFull, string finalThumb)>();
                        var baseExportsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports", partNumber);

                        // Step 1: Rename all files to temporary names to avoid collisions
                        for (int i = 0; i < orderedImageIds.Count; i++)
                        {
                            var imageId = orderedImageIds[i];
                            var newSequence = 103 + i;
                            var image = GetImageById(imageId);

                            if (image != null)
                            {
                                var tempGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
                                var tempFullPath = Path.Combine(baseExportsPath, $"{partNumber}.tmp_{tempGuid}.jpg");
                                var tempThumbPath = Path.Combine(baseExportsPath, $"{partNumber}.tmp_{tempGuid}.thumb.jpg");
                                var finalFullPath = Path.Combine(baseExportsPath, $"{partNumber}.{newSequence:000}.jpg");
                                var finalThumbPath = Path.Combine(baseExportsPath, $"{partNumber}.{newSequence:000}.thumb.jpg");

                                // Rename to temporary names
                                if (File.Exists(image.FullPath))
                                {
                                    File.Move(image.FullPath, tempFullPath);
                                }
                                if (!string.IsNullOrEmpty(image.ThumbPath) && File.Exists(image.ThumbPath))
                                {
                                    File.Move(image.ThumbPath, tempThumbPath);
                                }

                                tempFileMap[imageId] = (tempFullPath, tempThumbPath, finalFullPath, finalThumbPath);
                            }
                        }

                        // Step 2: Rename from temporary to final names
                        for (int i = 0; i < orderedImageIds.Count; i++)
                        {
                            var imageId = orderedImageIds[i];
                            var newSequence = 103 + i;

                            if (tempFileMap.TryGetValue(imageId, out var paths))
                            {
                                // Rename from temp to final
                                if (File.Exists(paths.tempFull))
                                {
                                    if (File.Exists(paths.finalFull)) File.Delete(paths.finalFull);
                                    File.Move(paths.tempFull, paths.finalFull);
                                }
                                if (File.Exists(paths.tempThumb))
                                {
                                    if (File.Exists(paths.finalThumb)) File.Delete(paths.finalThumb);
                                    File.Move(paths.tempThumb, paths.finalThumb);
                                }

                                // Update database
                                UpdateImageSequence(imageId, newSequence, paths.finalFull, paths.finalThumb);
                                logger?.Invoke($"Resequenced {imageId}: seq={newSequence}, file={Path.GetFileName(paths.finalFull)}");
                            }
                        }

                        transaction.Commit();
                        logger?.Invoke($"Resequencing completed successfully for part {partNumber}");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        logger?.Invoke($"Resequencing failed: {ex.Message}");
                        
                        // Best-effort cleanup of any temp files
                        try
                        {
                            var tempFiles = Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports", partNumber), "*.tmp_*");
                            foreach (var tempFile in tempFiles)
                            {
                                try { File.Delete(tempFile); } catch { }
                            }
                        }
                        catch { }
                        
                        throw;
                    }
                }
            }
        }

        #endregion
    }
}
