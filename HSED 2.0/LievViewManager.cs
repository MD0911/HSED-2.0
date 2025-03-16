using HSED_2_0;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Linq;

namespace HSED_2._0
{
    internal class LievViewManager
    {
        // Diese Eigenschaften werden aus dem MonetoringManager übernommen.
        public int BootFloor { get; private set; }
        public int TopFloor { get; private set; }
        public int GesamtFloor { get; private set; }

        public int[] IngrementEtage { get; private set; }

        // Hier wird das zusammengesetzte SVG als String gespeichert.
        public string ComposedSvg { get; private set; }

        // Pfad zur SVG-Datei, die eine einzelne Etage darstellt.
        public string SingleFloorSvgPath { get; set; } = "C:\\Users\\Mouad Ezzouine\\source\\repos\\HSED 2.0\\HSED 2.0\\Animation\\forBuild\\Schacht.svg";

        /// <summary>
        /// Liest die Floor-Werte aus dem MonetoringManager aus.
        /// </summary>
        public void Initialize()
        {
            // Übernehme die statischen Werte aus dem MonetoringManager
            BootFloor = MonetoringManager.BootFloor;
            TopFloor = MonetoringManager.TopFloor;
            GesamtFloor = MonetoringManager.GesamtFloor;
            SetIngrementEtage();

            Console.WriteLine($"LievViewManager: BootFloor = {BootFloor}, TopFloor = {TopFloor}, GesamtFloor = {GesamtFloor}");
        }

        public void SetIngrementEtage()
{
    int GesamtFloor = MonetoringManager.GesamtFloor;

    // Initialisieren des Arrays, falls nicht schon erfolgt.
    IngrementEtage = new int[GesamtFloor];

    for (int i = 1; i < GesamtFloor; i++)
    {
        byte[] increment = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x29, (byte)i });

        if (increment == null || increment.Length < 10)
        {
            Debug.WriteLine($"Ungültige Antwort für Etage {i}");
            continue;
        }

        IngrementEtage[i] = BitConverter.ToInt32(new byte[] { increment[10], increment[11], increment[12], increment[13] });
        Debug.WriteLine($"Increment Etage {i}: {IngrementEtage[i]}");
    }
}

        /// <summary>
        /// Baut aus dem Einzel-SVG für eine Etage ein zusammengesetztes SVG,
        /// in dem die Etagen übereinander angeordnet sind.
        /// </summary>
        public void BuildSchachtSvg()
        {
            // Lade das SVG der einzelnen Etage
            string floorSvgContent = File.ReadAllText(SingleFloorSvgPath);
            XDocument floorSvgDoc = XDocument.Parse(floorSvgContent);

            // Bestimme die Höhe der Etage (wir gehen davon aus, dass das root <svg> ein "height"-Attribut hat)
            double floorHeight = GetFloorHeight(floorSvgDoc);
            // Gesamthöhe des Schachts = Höhe einer Etage * Anzahl der Etagen
            double totalHeight = floorHeight * GesamtFloor;

            // Namespace definieren (SVG-Namespace)
            XNamespace svgNs = "http://www.w3.org/2000/svg";

            // Erstelle das Root-Element für das zusammengesetzte SVG
            XElement composedSvg = new XElement(svgNs + "svg",
                new XAttribute("xmlns", svgNs.NamespaceName),
                new XAttribute("width", floorSvgDoc.Root.Attribute("width")?.Value ?? "auto"),
                new XAttribute("height", totalHeight)
            );

            // Für jede Etage wird ein <g>-Element (Gruppe) mit entsprechender vertikaler Translation eingefügt.
            for (int i = 0; i < GesamtFloor; i++)
            {
                // Verschiebe jede Etage um i * floorHeight nach unten.
                XElement group = new XElement(svgNs + "g",
                    new XAttribute("transform", $"translate(0, {i * floorHeight})")
                );

                // Kopiere alle untergeordneten Elemente des einzelnen Etagen-SVGs in die Gruppe.
                foreach (XElement element in floorSvgDoc.Root.Elements())
                {
                    group.Add(new XElement(element));
                }

                composedSvg.Add(group);
            }

            // Speichere das zusammengesetzte SVG als String
            ComposedSvg = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), composedSvg).ToString();
        }

        /// <summary>
        /// Extrahiert die Höhe aus dem "height"-Attribut des SVG-Root-Elements.
        /// </summary>
        /// <param name="svgDoc">Das SVG-Dokument</param>
        /// <returns>Höhe als double</returns>
        private double GetFloorHeight(XDocument svgDoc)
        {
            // Zunächst versuchen wir, ein "height"-Attribut zu lesen.
            string heightStr = svgDoc.Root.Attribute("height")?.Value;
            if (!string.IsNullOrWhiteSpace(heightStr))
            {
                heightStr = heightStr.Replace("px", "").Trim();
                if (double.TryParse(heightStr, out double height))
                {
                    return height;
                }
            }

            // Wenn kein "height" vorhanden ist, extrahieren wir die Höhe aus dem viewBox.
            string viewBoxStr = svgDoc.Root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(viewBoxStr))
            {
                // Das viewBox-Attribut hat das Format "minX minY width height"
                string[] parts = viewBoxStr.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 && double.TryParse(parts[3], out double viewBoxHeight))
                {
                    // Da du festgestellt hast, dass der viewBox-Wert zu groß ist, 
                    // gibst du hier den effektiven Wert ein (z. B. 200)
                    double effectiveFloorHeight = 325; // Anpassen, je nachdem was wirklich passt
                    return effectiveFloorHeight;
                }
            }

            throw new InvalidOperationException("Die Höhe der Etage konnte nicht ermittelt werden.");
        }


        /// <summary>
        /// Ruft alle nötigen Methoden in der richtigen Reihenfolge auf, um die Schachtansicht
        /// vollständig vorzubereiten. Optional kann hier auch direkt in eine Datei gespeichert werden.
        /// </summary>
        public void PrepareSchacht()
        {
            // Initialisiere die Floor-Werte
            Initialize();
            // Baue das zusammengesetzte SVG
            BuildSchachtSvg();

            // Optional: Speichern in eine Datei, falls benötigt:
            // string outputPath = Path.Combine(AppContext.BaseDirectory, "Output", "SchachtComposed.svg");
            // File.WriteAllText(outputPath, ComposedSvg);
        }
    }
}
