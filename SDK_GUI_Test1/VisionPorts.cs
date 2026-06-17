namespace SDK_GUI_Test1
{
    /// <summary>
    /// Endpoints (port-stier) kopieret fra VPM. Bruges af AddOn-kontrollerne.
    /// Hentet via hoejreklik paa parameter-raekke -> "Copy text ... to clipboard".
    /// </summary>
    public static class VisionPorts
    {
        // Billede.
        public const string IMAGE = "Inspection.Image In Task:Image";

        // Task der skal re-koeres ved Reprocess (re-evaluerer NUVAERENDE billede,
        // tager IKKE et nyt). Bruges af CallRunOnTask_Sync i ViewModel.Reprocess.
        public const string IMAGE_IN_TASK = "Inspection.Image In Task";

        // Gauge pass/fail. VIGTIGT: Gauge har IKKE et "Passed"-port (kun Line Find har det).
        // Gauge bruger "All In Tolerance" (bekraeftet i mirror-filen ILineGaugeTool).
        public const string GAUGE_RESULT = "Inspection.Image In Task.Gauge:All In Tolerance";

        // Aktuel maalt afstand: ENKELT-vaerdi under-felt af Distance List (Real).
        public const string GAUGE_MEASURED = "Inspection.Image In Task.Gauge:Distance List.element";

        // Hele Distance List-porten (Real-liste) - bruges til typed read i ViewModel.
        public const string GAUGE_DISTANCE_LIST = "Inspection.Image In Task.Gauge:Distance List";

        // Gauge tolerance: HELE Tolerance-porten (VisionTolerance).
        // PropertyTolerance redigerer denne som een enhed (minus/nominal/plus).
        public const string GAUGE_TOLERANCE = "Inspection.Image In Task.Gauge:Distance Tolerance";

        // Under-felter (bruges KUN hvis man vil binde PropertyNumeric til et enkelt felt
        // til VISNING - de kan ikke skrives paalideligt individuelt).
        public const string GAUGE_NOMINAL = "Inspection.Image In Task.Gauge:Distance Tolerance.nominal";
        public const string GAUGE_MINUS = "Inspection.Image In Task.Gauge:Distance Tolerance.minus";
        public const string GAUGE_PLUS = "Inspection.Image In Task.Gauge:Distance Tolerance.plus";

        // ---------- BLOB ----------
        // Required Number Of Blobs: Range 1D (min/max antal blobs). Hele porten
        // redigeres som een enhed (VisionRange), ligesom Gauge-tolerancen.
        public const string BLOB_REQUIRED = "Inspection.Image In Task.Blob:Required Number Of Blobs";

        // Aktuelt antal fundne blobs (Integer) - til visning.
        public const string BLOB_COUNT = "Inspection.Image In Task.Blob:Output Blob List.count";

        // Blob pass/fail. Blob HAR en Passed-port (modsat Gauge).
        public const string BLOB_RESULT = "Inspection.Image In Task.Blob:Passed";
    }
}