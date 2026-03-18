using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSED_2_0
{
    public static class AsciiLoader
    {
        // Cache, um bereits geladene Bitmaps wiederzuverwenden.
        private static Dictionary<byte, Bitmap> _cache = new Dictionary<byte, Bitmap>();

        /// <summary>
        /// Lädt basierend auf dem übergebenen Byte den entsprechenden Bitmap.
        /// Erzeugt den Dateinamen im Format "ASCIIXX.bmp", wobei XX der Hexwert ist.
        /// Der Bitmap wird aus dem Ordner "bitmap" geladen.
        /// </summary>
        public static Bitmap LoadAsciiBitmap(byte value)
        {
            if (_cache.ContainsKey(value))
            {
                Console.WriteLine($"[AsciiLoader] Cache hit for value: {value:X2}");
                return _cache[value];
            }

            string hex = value.ToString("X2");
            string fileName = $"ASCII{hex}.bmp";
            // Verwende einen absoluten Pfad basierend auf dem aktuellen Arbeitsverzeichnis
            string folder = Path.Combine(AppContext.BaseDirectory, "bitmap");
            string path = Path.Combine(folder, fileName);

            Console.WriteLine($"[AsciiLoader] Versuche, Datei zu laden: {path}");
            if (!File.Exists(path))
            {
                string fallbackPath = Path.Combine(folder, "ASCII20.bmp");
                Console.WriteLine($"[AsciiLoader] Datei nicht gefunden: {path}");

                if (!File.Exists(fallbackPath))
                    throw new FileNotFoundException($"Weder {path} noch Fallback {fallbackPath} wurden gefunden.");

                Console.WriteLine($"[AsciiLoader] Verwende Fallback fuer {value:X2}: {fallbackPath}");
                Bitmap fallbackBmp = new Bitmap(fallbackPath);
                _cache[value] = fallbackBmp;
                return fallbackBmp;
            }

            Bitmap bmp = new Bitmap(path);
            Console.WriteLine($"[AsciiLoader] Datei erfolgreich geladen: {path}");
            _cache[value] = bmp;
            return bmp;
        }
    }
}
