using CCSuitePro.Services;

namespace CCSuitePro.Helpers
{
    /// <summary>
    /// Resuelve la categoría usando 3 niveles: Nombre/Carpeta → Contenido → Tamaño
    /// </summary>
    public static class CategoryResolver
    {
        private static SizeAnalyzer? _sizeAnalyzer;
        private static SizeAnalyzer SizeAnalyzerInstance => _sizeAnalyzer ??= new SizeAnalyzer();

        public static string Resolve(string filePath)
        {
            // Nivel 1: Nombre/Carpeta
            var category = FileHelper.GetCategoryFromPath(filePath);
            if (category != "Other") return category;

            // Nivel 2: Contenido interno
            var analyzed = FileHelper.GetCategoryWithAnalysis(filePath);
            if (analyzed != "Other") return analyzed;

            // Nivel 3: Tamaño
            return SizeAnalyzerInstance.GuessCategoryBySize(filePath);
        }
    }
}
