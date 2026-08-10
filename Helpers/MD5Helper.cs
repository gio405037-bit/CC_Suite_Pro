using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace CCSuitePro.Helpers
{
    public static class MD5Helper
    {
        public static async Task<string> CalculateMD5Async(string filePath)
        {
            return await Task.Run(() =>
            {
                using (var md5 = MD5.Create())
                using (var stream = new BufferedStream(File.OpenRead(filePath), 1048576))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            });
        }
        
        public static string CalculateMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
