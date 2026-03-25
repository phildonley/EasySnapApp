using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace EasySnapApp.Data
{
    /// <summary>
    /// SQLite database initialization and connection management for EasySnapApp
    /// Phase 2: Local persistence of capture sessions and images
    /// </summary>
    public class EasySnapDb
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public EasySnapDb()
        {
            // Store database in a writable per-user data folder (portable across install locations)
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EasySnapApp",
                "Data");

            Directory.CreateDirectory(dataFolder);

            _dbPath = Path.Combine(dataFolder, "EasySnap.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";
        }

        public string DatabasePath => _dbPath;

        /// <summary>
        /// Initialize database - create tables if they don't exist
        /// </summary>
        public void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Create CaptureSessions table
                var createSessionsTable = @"
                    CREATE TABLE IF NOT EXISTS CaptureSessions (
                        SessionId TEXT PRIMARY KEY,
                        PartNumber TEXT NOT NULL,
                        StartTimeUtc TEXT NOT NULL,
                        EndTimeUtc TEXT NULL,
                        IsActive INTEGER NOT NULL DEFAULT 1
                    );";

                // Create CapturedImages table
                var createImagesTable = @"
                    CREATE TABLE IF NOT EXISTS CapturedImages (
                        ImageId TEXT PRIMARY KEY,
                        SessionId TEXT NOT NULL,
                        PartNumber TEXT NOT NULL,
                        Sequence INTEGER NOT NULL,
                        FullPath TEXT NOT NULL,
                        ThumbPath TEXT NULL,
                        CaptureTimeUtc TEXT NOT NULL,
                        FileSizeBytes INTEGER NOT NULL,
                        WidthPx INTEGER NULL,
                        HeightPx INTEGER NULL,
                        WeightGrams REAL NULL,
                        DimX REAL NULL,
                        DimY REAL NULL,
                        DimZ REAL NULL,
                        IsDeleted INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (SessionId) REFERENCES CaptureSessions(SessionId)
                    );";

                // Create indexes
                var createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_images_part_seq ON CapturedImages(PartNumber, Sequence);
                    CREATE INDEX IF NOT EXISTS idx_images_session ON CapturedImages(SessionId);
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_images_unique_part_seq ON CapturedImages(PartNumber, Sequence, IsDeleted) WHERE IsDeleted=0;";

                using (var command = new SQLiteCommand(createSessionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(createImagesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(createIndexes, connection))
                {
                    command.ExecuteNonQuery();
                }

                // SOFT DELETE: Add DeletedAt column migration (safe to run repeatedly)
                AddDeletedAtColumnIfNeeded(connection);
                // EXPORT TRACKING: Add LastExportedAt column migration
                AddLastExportedAtColumnIfNeeded(connection);
                CreatePartsDataTable(connection);
            }
        }

        /// <summary>
        /// Add DeletedAt column to CapturedImages if it doesn't exist (defensive migration)
        /// </summary>
        private void AddDeletedAtColumnIfNeeded(SQLiteConnection connection)
        {
            try
            {
                // Check if DeletedAt column exists
                var checkColumnSql = "PRAGMA table_info(CapturedImages)";
                bool columnExists = false;

                using (var command = new SQLiteCommand(checkColumnSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var columnName = reader.GetString(1); // Column name is at index 1
                            if (columnName.Equals("DeletedAt", StringComparison.OrdinalIgnoreCase))
                            {
                                columnExists = true;
                                break;
                            }
                        }
                    }
                }

                if (!columnExists)
                {
                    var addColumnSql = "ALTER TABLE CapturedImages ADD COLUMN DeletedAt TEXT NULL";
                    using (var command = new SQLiteCommand(addColumnSql, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("SoftDelete: Added DeletedAt column to CapturedImages table");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SoftDelete: Migration error: {ex.Message}");
                // Don't throw - app should continue working even if migration fails
            }
        }

        /// <summary>
        /// Add LastExportedAt column to CapturedImages if it doesn't exist (export tracking migration)
        /// </summary>
        private void AddLastExportedAtColumnIfNeeded(SQLiteConnection connection)
        {
            try
            {
                if (!ColumnExists(connection, "CapturedImages", "LastExportedAt"))
                {
                    var addColumnSql = "ALTER TABLE CapturedImages ADD COLUMN LastExportedAt TEXT NULL";
                    using (var command = new SQLiteCommand(addColumnSql, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("ExportTracking: Added LastExportedAt column to CapturedImages table");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportTracking: Migration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a new database connection
        /// </summary>
        public SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(_connectionString);
        }
        /// <summary>
        /// PHASE 1: Create PartsData table for dynamic file naming system
        /// </summary>
        private void CreatePartsDataTable(SQLiteConnection connection)
        {
            try
            {
                // Create base PartsData table with core fields
                var createPartsDataTable = @"
                    CREATE TABLE IF NOT EXISTS PartsData (
                        PartNumber TEXT PRIMARY KEY,
                        TmsId TEXT NULL,
                        DisplayName TEXT NULL,
                        Description TEXT NULL,
                        ImportedAt TEXT NOT NULL,
                        ImportSourceFile TEXT NULL,
                        ImportRowIndex INTEGER NULL
                    );";

                using (var command = new SQLiteCommand(createPartsDataTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create indexes for fast lookups
                var createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_partsdata_tmsid ON PartsData(TmsId);
                    CREATE INDEX IF NOT EXISTS idx_partsdata_displayname ON PartsData(DisplayName);
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_partsdata_partnumber ON PartsData(PartNumber);";

                using (var command = new SQLiteCommand(createIndexes, connection))
                {
                    command.ExecuteNonQuery();
                }

                System.Diagnostics.Debug.WriteLine("PartsData table created successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PartsData table creation error: {ex.Message}");
                // Don't throw - app should continue working even if table creation fails
            }
        }

        /// <summary>
        /// PHASE 1: Add dynamic column to PartsData table
        /// </summary>
        public void AddPartsDataColumn(string columnName, string columnType = "TEXT")
        {
            if (string.IsNullOrEmpty(columnName))
                return;

            // Sanitize column name for SQL safety
            var safeName = SanitizeColumnName(columnName);

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                try
                {
                    // Check if column already exists
                    if (!ColumnExists(connection, "PartsData", safeName))
                    {
                        var addColumnSql = $"ALTER TABLE PartsData ADD COLUMN [{safeName}] {columnType} NULL";
                        using (var command = new SQLiteCommand(addColumnSql, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                        System.Diagnostics.Debug.WriteLine($"Added column [{safeName}] to PartsData table");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error adding column [{safeName}]: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// PHASE 1: Check if column exists in table
        /// </summary>
        private bool ColumnExists(SQLiteConnection connection, string tableName, string columnName)
        {
            var checkColumnSql = $"PRAGMA table_info({tableName})";

            using (var command = new SQLiteCommand(checkColumnSql, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var existingColumnName = reader.GetString(1); // Column name is at index 1
                        if (existingColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// PHASE 1: Sanitize column name for SQL safety
        /// </summary>
        public string SanitizeColumnName(string columnName)
        {
            if (string.IsNullOrEmpty(columnName))
                return "Unknown";

            // Remove or replace problematic characters
            var sanitized = columnName.Trim()
                .Replace(" ", "_")
                .Replace(".", "_")
                .Replace("-", "_")
                .Replace("(", "_")
                .Replace(")", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("[", "_")
                .Replace("]", "_");

            // Ensure it starts with a letter or underscore
            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            {
                sanitized = "Col_" + sanitized;
            }

            // Limit length to 64 characters
            if (sanitized.Length > 64)
            {
                sanitized = sanitized.Substring(0, 64);
            }

            return sanitized;
        }

        /// <summary>
        /// PHASE 1: Get list of all columns in PartsData table
        /// </summary>
        public List<string> GetPartsDataColumns()
        {
            var columns = new List<string>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var pragmaSql = "PRAGMA table_info(PartsData)";
                using (var command = new SQLiteCommand(pragmaSql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var columnName = reader.GetString(1); // Column name is at index 1
                            columns.Add(columnName);
                        }
                    }
                }
            }

            return columns;
        }
    }
}
