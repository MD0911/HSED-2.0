using Avalonia.Threading;
using HSED_2_0;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HSED_2._0
{

   
    internal class LiveViewAnimator
{

        public void Start()
        {
            calcPosition();
        }

        private void setPosition(int position)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentZustand = position;
                }
            });
        }

        private void calcPosition()
        {
            int gesamtFloor = MonetoringManager.GesamtFloor;
            // Erstelle eine Instanz von LievViewManager und rufe PrepareSchacht() auf
            var lvManager = new LievViewManager();
            lvManager.PrepareSchacht(); // Dadurch wird ComposedSvg gesetzt

            //int[] incrementEtage = lvManager.IngrementEtage;
            int etageHoehe = 325; // Fallback oder alternative Berechnung

            int fahrkrobPosition = MainWindow.MainViewModelInstance.CurrentFahrkorb;

            // Jetzt sollte lvManager.ComposedSvg nicht mehr null sein.
            double floorHeight = GetFloorHeight(lvManager.ComposedSvg, gesamtFloor);
            Debug.WriteLine($"FloorHeight: {floorHeight}");
        }

        public static double GetFloorHeight(string svgContent, int numberOfFloors)
        {
            if (numberOfFloors <= 0)
                throw new ArgumentException("Die Etagenanzahl muss größer als 0 sein.");

            // Lade das SVG als XML-Dokument
            XDocument doc = XDocument.Parse(svgContent);
            XElement svgElement = doc.Root;
            if (svgElement == null)
                throw new Exception("Das SVG-Dokument ist ungültig.");

            // Lese das "height"-Attribut aus
            string heightAttr = svgElement.Attribute("height")?.Value;
            if (string.IsNullOrEmpty(heightAttr))
                throw new Exception("Kein 'height'-Attribut im SVG gefunden.");

            // Entferne eventuelle Einheiten (z.B. "px")
            heightAttr = heightAttr.Replace("px", "").Trim();

            // Versuche, den Wert zu parsen
            if (!double.TryParse(heightAttr, out double totalHeight))
                throw new Exception("Der 'height'-Wert im SVG ist ungültig.");

            // Berechne die Höhe einer Etage
            double floorHeight = totalHeight / numberOfFloors;
            return floorHeight;
        }
    }




}

