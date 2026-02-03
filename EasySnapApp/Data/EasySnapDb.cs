using System;
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
        /// Get a new database connection
        /// </summary>
        public SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(_connectionString);
        }
    }
}
