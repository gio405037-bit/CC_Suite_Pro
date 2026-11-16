using CCSuitePro.Services;

namespace CCSuitePro.Helpers
{
    /// <summary>Single entry point for the conservative native classifier.</summary>
    public static class CategoryResolver
    {
        private static readonly ClassificationEngine Classifier = new();

        public static string Resolve(string filePath)
        {
            return Classifier.Classify(filePath).PrimaryCategory;
        }
    }
}
