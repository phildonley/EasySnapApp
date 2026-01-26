using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using EasySnapApp.Models;

namespace EasySnapApp.Repositories
{
    public class SQLiteImageRepository : IImageRepository
    {
        private readonly string _connectionString;

        public SQLiteImageRepository(string databasePath = "EasySnapApp.db")
        {
            _connectionString = $"Data Source={databasePath};Version=3;";
            EnsureDatabaseExists();
        }

        private void EnsureDatabaseExists()
        {
            if (!File.Exists(Path.GetFileName(_connectionString.Split('=')[1].Split(';')[0])))
            {
                CreateDatabase();
            }
        }

        private void CreateDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS Images (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PartNumber TEXT NOT NULL,
                        Sequence INTEGER NOT NULL,
                        FullPath TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        CaptureTimeUtc TEXT NOT NULL,
                        FileSizeBytes INTEGER NOT NULL,
                        Weight REAL,
                        DimX REAL,
                        DimY REAL,
                        DimZ REAL,
                        Metadata TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_Images_PartNumber ON Images(PartNumber);
                    CREATE INDEX IF NOT EXISTS IX_Images_CaptureTimeUtc ON Images(CaptureTimeUtc);
                ";
                
                using (var command = new SQLiteCommand(createTableSql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public async Task<List<ImageRecord>> GetAllImagesAsync()
        {
            var images = new List<ImageRecord>();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT * FROM Images ORDER BY PartNumber, Sequence";
                
                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        images.Add(MapReaderToImageRecord(reader));
                    }
                }
            }
            
            return images;
        }

        public async Task<List<ImageRecord>> GetImagesByPartNumberAsync(string partNumber)
        {
            var images = new List<ImageRecord>();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT * FROM Images WHERE PartNumber = @PartNumber ORDER BY Sequence";
                
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@PartNumber", partNumber);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            images.Add(MapReaderToImageRecord(reader));
                        }
                    }
                }
            }
            
            return images;
        }

        public async Task<List<string>> GetDistinctPartNumbersAsync()
        {
            var partNumbers = new List<string>();
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT DISTINCT PartNumber FROM Images ORDER BY PartNumber";
                
                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        partNumbers.Add(Convert.ToString(reader["PartNumber"]));
                    }
                }
            }
            
            return partNumbers;
        }

        public async Task<ImageRecord> GetImageByIdAsync(int id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT * FROM Images WHERE Id = @Id";
                
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapReaderToImageRecord(reader);
                        }
                    }
                }
            }
            
            return null;
        }

        public async Task<int> AddImageAsync(ImageRecord image)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = @"
                    INSERT INTO Images (PartNumber, Sequence, FullPath, FileName, CaptureTimeUtc, FileSizeBytes, Weight, DimX, DimY, DimZ, Metadata)
                    VALUES (@PartNumber, @Sequence, @FullPath, @FileName, @CaptureTimeUtc, @FileSizeBytes, @Weight, @DimX, @DimY, @DimZ, @Metadata);
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(sql, connection))
                {
                    AddParametersToCommand(command, image);
                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }

        public async Task<bool> UpdateImageAsync(ImageRecord image)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = @"
                    UPDATE Images SET 
                        PartNumber = @PartNumber,
                        Sequence = @Sequence,
                        FullPath = @FullPath,
                        FileName = @FileName,
                        CaptureTimeUtc = @CaptureTimeUtc,
                        FileSizeBytes = @FileSizeBytes,
                        Weight = @Weight,
                        DimX = @DimX,
                        DimY = @DimY,
                        DimZ = @DimZ,
                        Metadata = @Metadata
                    WHERE Id = @Id";
                
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", image.Id);
                    AddParametersToCommand(command, image);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

        public async Task<bool> DeleteImageAsync(int id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var sql = "DELETE FROM Images WHERE Id = @Id";
                
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

        private void AddParametersToCommand(SQLiteCommand command, ImageRecord image)
        {
            command.Parameters.AddWithValue("@PartNumber", image.PartNumber ?? string.Empty);
            command.Parameters.AddWithValue("@Sequence", image.Sequence);
            command.Parameters.AddWithValue("@FullPath", image.FullPath ?? string.Empty);
            command.Parameters.AddWithValue("@FileName", image.FileName ?? string.Empty);
            command.Parameters.AddWithValue("@CaptureTimeUtc", image.CaptureTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            command.Parameters.AddWithValue("@FileSizeBytes", image.FileSizeBytes);
            command.Parameters.AddWithValue("@Weight", image.Weight.HasValue ? (object)image.Weight.Value : DBNull.Value);
            command.Parameters.AddWithValue("@DimX", image.DimX.HasValue ? (object)image.DimX.Value : DBNull.Value);
            command.Parameters.AddWithValue("@DimY", image.DimY.HasValue ? (object)image.DimY.Value : DBNull.Value);
            command.Parameters.AddWithValue("@DimZ", image.DimZ.HasValue ? (object)image.DimZ.Value : DBNull.Value);
            command.Parameters.AddWithValue("@Metadata", image.Metadata ?? string.Empty);
        }

        private ImageRecord MapReaderToImageRecord(System.Data.Common.DbDataReader reader)
        {
            return new ImageRecord
            {
                Id = Convert.ToInt32(reader["Id"]),
                PartNumber = Convert.ToString(reader["PartNumber"]),
                Sequence = Convert.ToInt32(reader["Sequence"]),
                FullPath = Convert.ToString(reader["FullPath"]),
                FileName = Convert.ToString(reader["FileName"]),
                CaptureTimeUtc = DateTime.Parse(Convert.ToString(reader["CaptureTimeUtc"])),
                FileSizeBytes = Convert.ToInt64(reader["FileSizeBytes"]),
                Weight = reader["Weight"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Weight"]),
                DimX = reader["DimX"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["DimX"]),
                DimY = reader["DimY"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["DimY"]),
                DimZ = reader["DimZ"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["DimZ"]),
                Metadata = Convert.ToString(reader["Metadata"])
            };
        }
    }
}
