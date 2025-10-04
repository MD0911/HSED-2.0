using HSED_2_0;
using System;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace HSED_2._0
{
    internal class LievViewManager
    {
        // Diese Eigenschaften werden aus dem MonetoringManager übernommen.
        public int BootFloor { get; private set; }
        public int TopFloor { get; private set; }
        public int GesamtFloor { get; private set; }
        public static int[] IngrementEtage { get; private set; }

        // Hier wird das zusammengesetzte SVG als String gespeichert.
        public string ComposedSvg { get; private set; }

        // Neu: Gesamt-Höhe des zusammengesetzten SVG (im Originalkoordinatensystem)
        public double TotalHeight { get; private set; }

        // Pfad zur SVG-Datei, die eine einzelne Etage darstellt.
        public string SingleFloorSvgPath { get; set; } = "Animation/forBuild/SchachtVorne.svg";
        public string AlternativeSingleFloorSvgPath { get; set; } = "Animation/forBuild/SchachtHinten.svg";

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

            Debug.WriteLine($"LievViewManager: BootFloor = {BootFloor}, TopFloor = {TopFloor}, GesamtFloor = {GesamtFloor}");
        }

        public void SetIngrementEtage()
        {
            int gesamtFloor = MonetoringManager.GesamtFloor;
            // Initialisieren des Arrays, falls nicht schon erfolgt.
            IngrementEtage = new int[gesamtFloor];

            for (int i = 1; i < gesamtFloor; i++)
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

            // Bestimme die Höhe der Etage (z.B. aus dem "height"-Attribut oder als Fallback 325)
            double floorHeight = GetFloorHeight(floorSvgDoc);
            // Gesamthöhe des Schachts = Höhe einer Etage * Anzahl der Etagen
            double totalHeight = floorHeight * GesamtFloor;
            TotalHeight = totalHeight;  // Speichern der Gesamthöhe

            // Namespace definieren (SVG-Namespace)
            XNamespace svgNs = "http://www.w3.org/2000/svg";

            // Erstelle das Root-Element für das zusammengesetzte SVG.
            // Hier setzen wir die "height"-Eigenschaft auf totalHeight,
            // sodass im Originalkoordinatensystem die volle Höhe abgebildet wird.
            XElement composedSvg = new XElement(svgNs + "svg",
                new XAttribute("xmlns", svgNs.NamespaceName),
                new XAttribute("width", floorSvgDoc.Root.Attribute("width")?.Value ?? "auto"),
                new XAttribute("height", totalHeight)
            );

            // Füge für jede Etage ein <g>-Element mit entsprechender vertikaler Translation hinzu.
            for (int i = 0; i < GesamtFloor - 2; i++)
            {
                XElement group = new XElement(svgNs + "g",
                    new XAttribute("transform", $"translate(0, {i * floorHeight})")
                );

                // Kopiere alle untergeordneten Elemente der Einzel-Etagen-SVG in die Gruppe.
                foreach (XElement element in floorSvgDoc.Root.Elements())
                {
                    group.Add(new XElement(element));
                }

                composedSvg.Add(group);
            }

            // Speichere das zusammengesetzte SVG als String.
            ComposedSvg = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), composedSvg).ToString();
        }

        /// <summary>
        /// Extrahiert die Höhe aus dem "height"-Attribut des SVG-Root-Elements.
        /// Falls nicht vorhanden, wird ein Fallback-Wert (325) verwendet.
        /// </summary>
        /// <param name="svgDoc">Das SVG-Dokument</param>
        /// <returns>Höhe als double</returns>
        private double GetFloorHeight(XDocument svgDoc)
        {
            string heightStr = svgDoc.Root.Attribute("height")?.Value;
            if (!string.IsNullOrWhiteSpace(heightStr))
            {
                heightStr = heightStr.Replace("px", "").Trim();
                if (double.TryParse(heightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double height))
                {
                    return height;
                }
            }

            // Falls kein gültiges "height" vorhanden ist, verwende den Fallback-Wert.
            return 325;
        }

        /// <summary>
        /// Baut alternativ ein zusammengesetztes SVG anhand eines alternativen SVG-Pfads.
        /// </summary>
        public void BuildSchachtSvgAlternative()
        {
            string floorSvgContent = File.ReadAllText(AlternativeSingleFloorSvgPath);
            XDocument floorSvgDoc = XDocument.Parse(floorSvgContent);

            double floorHeight = GetFloorHeight(floorSvgDoc);
            double totalHeight = floorHeight * GesamtFloor;

            XNamespace svgNs = "http://www.w3.org/2000/svg";

            XElement composedSvg = new XElement(svgNs + "svg",
                new XAttribute("xmlns", svgNs.NamespaceName),
                new XAttribute("width", floorSvgDoc.Root.Attribute("width")?.Value ?? "auto"),
                new XAttribute("height", totalHeight)
            );

            for (int i = 0; i < GesamtFloor - 2; i++)
            {
                XElement group = new XElement(svgNs + "g",
                    new XAttribute("transform", $"translate(0, {i * floorHeight})")
                );

                foreach (XElement element in floorSvgDoc.Root.Elements())
                {
                    group.Add(new XElement(element));
                }

                composedSvg.Add(group);
            }

            ComposedSvgAlternative = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), composedSvg).ToString();
        }

        // Neue Eigenschaft für das alternative zusammengesetzte SVG.
        public string ComposedSvgAlternative { get; private set; }

        /// <summary>
        /// Ruft alle nötigen Methoden in der richtigen Reihenfolge auf, um die Schachtansicht vorzubereiten.
        /// </summary>
        public void PrepareSchacht()
        {
            Initialize();
            BuildSchachtSvg();
            BuildSchachtSvgAlternative();

            // Optional: Speichere das zusammengesetzte SVG in eine Datei.
            //string outputPath = Path.Combine(AppContext.BaseDirectory, "Output", "SchachtComposed.svg");
           // File.WriteAllText(outputPath, ComposedSvg);
        }
    }
}
