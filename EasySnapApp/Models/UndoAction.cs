using System;
using System.Collections.Generic;
using EasySnapApp.Data;

namespace EasySnapApp.Models
{
    /// <summary>
    /// Represents an undo action for deleted images
    /// </summary>
    public class UndoAction
    {
        public string ActionId { get; set; }
        public DateTime Timestamp { get; set; }
        public UndoActionType ActionType { get; set; }
        public List<DeletedImageInfo> DeletedImages { get; set; } = new List<DeletedImageInfo>();
        public string RecycleFolderPath { get; set; }
        public string Description { get; set; }
    }

    public enum UndoActionType
    {
        Delete
    }

    /// <summary>
    /// Information about a deleted image for restoration
    /// </summary>
    public class DeletedImageInfo
    {
        // Original paths
        public string OriginalFullPath { get; set; }
        public string OriginalThumbPath { get; set; }
        
        // Recycled paths
        public string RecycledFullPath { get; set; }
        public string RecycledThumbPath { get; set; }
        
        // DB record for restoration
        public CapturedImage OriginalDbRecord { get; set; }
        
        // UI collection index for insertion
        public int OriginalIndex { get; set; }
    }
}
