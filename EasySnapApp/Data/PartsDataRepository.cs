using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;

namespace EasySnapApp.Data
{
    /// <summary>
    /// PHASE 1: Data models for parts data import system
    /// </summary>
    public class PartDataRecord
    {
        public string PartNumber { get; set; }
        public string TmsId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public DateTime ImportedAt { get; set; }
        public string ImportSourceFile { get; set; }
        public int ImportRowIndex { get; set; }
        public Dictionary<string, string> AdditionalColumns { get; set; } = new Dictionary<string, string>();
    }

    public class ImportPreviewData
    {
        public List<string> Headers { get; set; } = new List<string>();
        public List<List<string>> PreviewRows { get; set; } = new List<List<string>>();
        public int TotalRowCount { get; set; }
        public int SelectedHeaderRowIndex { get; set; }
    }

    public class ImportMapping
    {
        public int PartNumberColumnIndex { get; set; } = -1;
        public int TmsIdColumnIndex { get; set; } = -1;
        public int DisplayNameColumnIndex { get; set; } = -1;
        public int DescriptionColumnIndex { get; set; } = -1;
        public List<int> AdditionalColumnIndexes { get; set; } = new List<int>();
    }

    public class ImportProgress
    {
        public int ProcessedRows { get; set; }
        public int TotalRows { get; set; }
        public int SuccessfulImports { get; set; }
        public int SkippedRows { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool IsComplete { get; set; }
        public TimeSpan ElapsedTime { get; set; }
    }

    /// <summary>
    /// PHASE 1: Repository for parts data CRUD operations
    /// Handles import of large CSV/Excel datasets for dynamic file naming
    /// </summary>
    public class PartsDataRepository
    {
        private readonly EasySnapDb _database;

        public PartsDataRepository(EasySnapDb database)
        {
            _database = database;
        }

        #region CSV/Excel Data Import

        /// <summary>
        /// Parse CSV file and detect header rows for preview
        /// </summary>
        public ImportPreviewData ParseCsvForPreview(string filePath, int maxPreviewRows = 10)
        {
            var preview = new ImportPreviewData();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    var allLines = new List<string>();
                    string line;
                    int totalLines = 0;

                    // Read all lines for analysis
                    while ((line = reader.ReadLine()) != null && totalLines < 1000) // Limit to first 1000 lines for preview
                    {
                        allLines.Add(line);
                        totalLines++;
                    }

                    // Continue reading to get total count
                    while (reader.ReadLine() != null)
                    {
                        totalLines++;
                    }

                    preview.TotalRowCount = totalLines;

                    if (allLines.Count == 0)
                        return preview;

                    // Parse CSV lines - handle quoted fields with commas
                    var parsedRows = new List<List<string>>();
                    foreach (var csvLine in allLines.Take(Math.Min(50, allLines.Count))) // First 50 rows for analysis
                    {
                        parsedRows.Add(ParseCsvLine(csvLine));
                    }

                    // Try to detect header row by looking for the row with most non-numeric values
                    var bestHeaderRowIndex = DetectHeaderRow(parsedRows);
                    preview.SelectedHeaderRowIndex = bestHeaderRowIndex;

                    if (bestHeaderRowIndex >= 0 && bestHeaderRowIndex < parsedRows.Count)
                    {
                        preview.Headers = parsedRows[bestHeaderRowIndex];

                        // Get preview data rows (skip header and any metadata rows before it)
                        var dataStartIndex = bestHeaderRowIndex + 1;
                        for (int i = dataStartIndex; i < parsedRows.Count && preview.PreviewRows.Count < maxPreviewRows; i++)
                        {
                            preview.PreviewRows.Add(parsedRows[i]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing CSV file: {ex.Message}", ex);
            }

            return preview;
        }

        /// <summary>
        /// Parse CSV file with specific header row selection - OPTIMIZED VERSION
        /// </summary>
        public ImportPreviewData ParseCsvWithHeaderRow(string filePath, int headerRowIndex, int maxPreviewRows = 10)
        {
            var preview = new ImportPreviewData();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    var lines = new List<string>();
                    string line;
                    int lineCount = 0;

                    // OPTIMIZATION: Only read header + preview rows (not 1000+ lines)
                    var maxLinesToRead = headerRowIndex + maxPreviewRows + 2;
                    while ((line = reader.ReadLine()) != null && lineCount < maxLinesToRead)
                    {
                        lines.Add(line);
                        lineCount++;
                    }

                    // OPTIMIZATION: Fast total count (no parsing)
                    int totalLines = lineCount;
                    while (reader.ReadLine() != null)
                    {
                        totalLines++;
                    }

                    preview.TotalRowCount = totalLines;
                    preview.SelectedHeaderRowIndex = headerRowIndex;

                    if (headerRowIndex >= 0 && headerRowIndex < lines.Count)
                    {
                        // Parse header row only
                        preview.Headers = ParseCsvLine(lines[headerRowIndex]);

                        // Parse preview data rows only
                        for (int i = headerRowIndex + 1; i < lines.Count && preview.PreviewRows.Count < maxPreviewRows; i++)
                        {
                            preview.PreviewRows.Add(ParseCsvLine(lines[i]));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing CSV file with header row {headerRowIndex}: {ex.Message}", ex);
            }

            return preview;
        }

        /// <summary>
        /// Execute bulk import of CSV data with progress reporting
        /// </summary>
        public void ImportCsvData(string filePath, int headerRowIndex, ImportMapping mapping,
            Action<ImportProgress> progressCallback = null, int batchSize = 1000)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var progress = new ImportProgress();
            var startTime = DateTime.Now;
            var fileName = Path.GetFileName(filePath);

            // First, prepare the database schema for selected columns
            PrepareImportSchema(filePath, headerRowIndex, mapping);

            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    // Skip to header row and read headers
                    var headers = new List<string>();
                    for (int i = 0; i <= headerRowIndex; i++)
                    {
                        var line = reader.ReadLine();
                        if (i == headerRowIndex)
                        {
                            headers = ParseCsvLine(line);
                            break;
                        }
                    }

                    // Get total row count for progress
                    var currentPosition = reader.BaseStream.Position;
                    int totalDataRows = 0;
                    while (reader.ReadLine() != null)
                    {
                        totalDataRows++;
                    }
                    progress.TotalRows = totalDataRows;

                    // Reset to data start position
                    reader.BaseStream.Position = currentPosition;
                    reader.DiscardBufferedData();

                    // Batch import data
                    using (var connection = _database.GetConnection())
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                var batch = new List<PartDataRecord>();
                                string dataLine;
                                int rowIndex = headerRowIndex + 1;

                                while ((dataLine = reader.ReadLine()) != null)
                                {
                                    try
                                    {
                                        var values = ParseCsvLine(dataLine);
                                        var record = MapCsvRowToRecord(values, headers, mapping, fileName, rowIndex);

                                        if (record != null && !string.IsNullOrEmpty(record.PartNumber))
                                        {
                                            batch.Add(record);
                                        }
                                        else
                                        {
                                            progress.SkippedRows++;
                                        }

                                        if (batch.Count >= batchSize)
                                        {
                                            InsertPartDataBatch(connection, transaction, batch, headers, mapping);
                                            progress.SuccessfulImports += batch.Count;
                                            batch.Clear();
                                        }

                                        progress.ProcessedRows++;
                                        rowIndex++;

                                        // Report progress periodically
                                        if (progress.ProcessedRows % 100 == 0)
                                        {
                                            progress.ElapsedTime = DateTime.Now - startTime;
                                            progressCallback?.Invoke(progress);
                                        }
                                    }
                                    catch (Exception rowEx)
                                    {
                                        progress.Errors.Add($"Row {rowIndex}: {rowEx.Message}");
                                        progress.SkippedRows++;
                                    }
                                }

                                // Insert final batch
                                if (batch.Count > 0)
                                {
                                    InsertPartDataBatch(connection, transaction, batch, headers, mapping);
                                    progress.SuccessfulImports += batch.Count;
                                }

                                transaction.Commit();
                                progress.IsComplete = true;
                            }
                            catch (Exception ex)
                            {
                                transaction.Rollback();
                                throw new Exception($"Import transaction failed: {ex.Message}", ex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                progress.Errors.Add($"Import failed: {ex.Message}");
                throw;
            }
            finally
            {
                progress.ElapsedTime = DateTime.Now - startTime;
                progressCallback?.Invoke(progress);
            }
        }

        #endregion

        #region Data Lookup Methods

        /// <summary>
        /// Fast lookup of part data by part number
        /// </summary>
        public PartDataRecord GetPartData(string partNumber)
        {
            if (string.IsNullOrEmpty(partNumber))
                return null;

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var sql = "SELECT * FROM PartsData WHERE PartNumber = @partNumber LIMIT 1";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return ReadPartDataRecord(reader);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get TMS ID for filename prefix
        /// </summary>
        public string GetTmsIdForPart(string partNumber)
        {
            var partData = GetPartData(partNumber);
            return partData?.TmsId;
        }

        /// <summary>
        /// Get display name for filename component
        /// </summary>
        public string GetDisplayNameForPart(string partNumber)
        {
            var partData = GetPartData(partNumber);
            return partData?.DisplayName;
        }

        /// <summary>
        /// Check if part data exists for a part number
        /// </summary>
        public bool HasPartData(string partNumber)
        {
            if (string.IsNullOrEmpty(partNumber))
                return false;

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM PartsData WHERE PartNumber = @partNumber";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Get count of imported part records
        /// </summary>
        public int GetPartDataCount()
        {
            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM PartsData";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// Search parts by TMS ID or part number
        /// </summary>
        public List<PartDataRecord> SearchParts(string searchTerm, int maxResults = 50)
        {
            var results = new List<PartDataRecord>();

            if (string.IsNullOrEmpty(searchTerm))
                return results;

            using (var connection = _database.GetConnection())
            {
                connection.Open();

                var sql = @"SELECT * FROM PartsData 
                           WHERE PartNumber LIKE @term OR TmsId LIKE @term OR DisplayName LIKE @term 
                           ORDER BY PartNumber 
                           LIMIT @maxResults";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@term", $"%{searchTerm}%");
                    command.Parameters.AddWithValue("@maxResults", maxResults);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(ReadPartDataRecord(reader));
                        }
                    }
                }
            }

            return results;
        }

        #endregion

        #region Private Helper Methods

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line))
                return result;

            var inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    // Handle quoted fields
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        currentField.Append('"');
                        i++; // Skip next quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // Field separator
                    result.Add(currentField.ToString().Trim());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            // Add final field
            result.Add(currentField.ToString().Trim());

            return result;
        }

        private int DetectHeaderRow(List<List<string>> rows)
        {
            if (rows.Count == 0)
                return -1;

            int bestRowIndex = 0;
            int maxNonNumericFields = 0;

            // Look at first 5 rows to find the one with most non-numeric values
            for (int i = 0; i < Math.Min(5, rows.Count); i++)
            {
                var row = rows[i];
                int nonNumericCount = 0;

                foreach (var field in row)
                {
                    if (!string.IsNullOrEmpty(field) && !double.TryParse(field, out _) && field.Length > 2)
                    {
                        nonNumericCount++;
                    }
                }

                if (nonNumericCount > maxNonNumericFields)
                {
                    maxNonNumericFields = nonNumericCount;
                    bestRowIndex = i;
                }
            }

            return bestRowIndex;
        }

        private void PrepareImportSchema(string filePath, int headerRowIndex, ImportMapping mapping)
        {
            // Parse headers to add dynamic columns
            using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var headers = new List<string>();
                for (int i = 0; i <= headerRowIndex; i++)
                {
                    var line = reader.ReadLine();
                    if (i == headerRowIndex)
                    {
                        headers = ParseCsvLine(line);
                        break;
                    }
                }

                // Add columns for additional selected fields
                foreach (var columnIndex in mapping.AdditionalColumnIndexes)
                {
                    if (columnIndex >= 0 && columnIndex < headers.Count)
                    {
                        _database.AddPartsDataColumn(headers[columnIndex]);
                    }
                }
            }
        }

        private PartDataRecord MapCsvRowToRecord(List<string> values, List<string> headers,
            ImportMapping mapping, string sourceFile, int rowIndex)
        {
            var record = new PartDataRecord
            {
                ImportedAt = DateTime.UtcNow,
                ImportSourceFile = sourceFile,
                ImportRowIndex = rowIndex
            };

            // Map core fields
            if (mapping.PartNumberColumnIndex >= 0 && mapping.PartNumberColumnIndex < values.Count)
            {
                record.PartNumber = values[mapping.PartNumberColumnIndex]?.Trim();
            }

            if (mapping.TmsIdColumnIndex >= 0 && mapping.TmsIdColumnIndex < values.Count)
            {
                record.TmsId = values[mapping.TmsIdColumnIndex]?.Trim();
            }

            if (mapping.DisplayNameColumnIndex >= 0 && mapping.DisplayNameColumnIndex < values.Count)
            {
                record.DisplayName = values[mapping.DisplayNameColumnIndex]?.Trim();
            }

            if (mapping.DescriptionColumnIndex >= 0 && mapping.DescriptionColumnIndex < values.Count)
            {
                record.Description = values[mapping.DescriptionColumnIndex]?.Trim();
            }

            // Map additional columns
            foreach (var columnIndex in mapping.AdditionalColumnIndexes)
            {
                if (columnIndex >= 0 && columnIndex < values.Count && columnIndex < headers.Count)
                {
                    var columnName = headers[columnIndex];
                    var columnValue = values[columnIndex]?.Trim();
                    record.AdditionalColumns[columnName] = columnValue;
                }
            }

            return record;
        }

        private void InsertPartDataBatch(SQLiteConnection connection, SQLiteTransaction transaction,
            List<PartDataRecord> batch, List<string> headers, ImportMapping mapping)
        {
            foreach (var record in batch)
            {
                // Build dynamic SQL based on available data
                var columns = new List<string> { "PartNumber", "TmsId", "DisplayName", "Description", "ImportedAt", "ImportSourceFile", "ImportRowIndex" };
                var parameters = new List<string> { "@partNumber", "@tmsId", "@displayName", "@description", "@importedAt", "@sourceFile", "@rowIndex" };

                // Add dynamic columns
                foreach (var kvp in record.AdditionalColumns)
                {
                    var sanitizedName = _database.SanitizeColumnName(kvp.Key);
                    columns.Add($"[{sanitizedName}]");
                    parameters.Add($"@{sanitizedName}");
                }

                var sql = $@"INSERT OR REPLACE INTO PartsData ({string.Join(", ", columns)}) 
                           VALUES ({string.Join(", ", parameters)})";

                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    // Core parameters
                    command.Parameters.AddWithValue("@partNumber", record.PartNumber ?? "");
                    command.Parameters.AddWithValue("@tmsId", record.TmsId ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@displayName", record.DisplayName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@description", record.Description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@importedAt", record.ImportedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@sourceFile", record.ImportSourceFile ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@rowIndex", record.ImportRowIndex);

                    // Dynamic parameters
                    foreach (var kvp in record.AdditionalColumns)
                    {
                        var sanitizedName = _database.SanitizeColumnName(kvp.Key);
                        command.Parameters.AddWithValue($"@{sanitizedName}", kvp.Value ?? (object)DBNull.Value);
                    }

                    command.ExecuteNonQuery();
                }
            }
        }

        private PartDataRecord ReadPartDataRecord(SQLiteDataReader reader)
        {
            var record = new PartDataRecord
            {
                PartNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                TmsId = reader.IsDBNull(1) ? null : reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                ImportedAt = DateTime.Parse(reader.GetString(4)),
                ImportSourceFile = reader.IsDBNull(5) ? null : reader.GetString(5),
                ImportRowIndex = reader.GetInt32(6)
            };

            // Read additional dynamic columns
            for (int i = 7; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var columnValue = reader.IsDBNull(i) ? null : reader.GetString(i);
                if (!string.IsNullOrEmpty(columnValue))
                {
                    record.AdditionalColumns[columnName] = columnValue;
                }
            }

            return record;
        }

        #endregion
        /// <summary>Phase 3: Return all part numbers for the preview combo in FileNameBuilderWindow</summary>
        public List<string> GetAllPartNumbers()
        {
            var results = new List<string>();
            using (var connection = _database.GetConnection())
            {
                connection.Open();
                var sql = "SELECT DISTINCT PartNumber FROM PartsData ORDER BY PartNumber ASC LIMIT 100";
                using (var cmd = new SQLiteCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        results.Add(reader.GetString(0));
            }
            return results;
        }
    }
}