using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

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
        /// Get the next sequence number for a part
        /// </summary>
        public int GetNextSequenceForPart(string partNumber)
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var selectSql = @"
                    SELECT COALESCE(MAX(Sequence), 102) + 1 as NextSequence
                    FROM CapturedImages 
                    WHERE PartNumber = @partNumber AND IsDeleted = 0";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    var result = command.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
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
    }
}
