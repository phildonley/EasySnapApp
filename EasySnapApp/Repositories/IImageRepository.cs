using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasySnapApp.Models;

namespace EasySnapApp.Repositories
{
    public interface IImageRepository
    {
        Task<List<ImageRecord>> GetAllImagesAsync();
        Task<List<ImageRecord>> GetImagesByPartNumberAsync(string partNumber);
        Task<List<string>> GetDistinctPartNumbersAsync();
        Task<ImageRecord> GetImageByIdAsync(int id);
        Task<int> AddImageAsync(ImageRecord image);
        Task<bool> UpdateImageAsync(ImageRecord image);
        Task<bool> DeleteImageAsync(int id);
    }
}
