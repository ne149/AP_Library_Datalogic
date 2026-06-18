namespace SDK_GUI_Test1
{
    /// <summary>
    /// Endpoints (port paths) copied from the VPM. Used by the AddOn controls.
    /// Obtained via right-click on a parameter row -> "Copy text ... to clipboard".
    /// </summary>
    public static class VisionPorts
    {
        // Image.
        public const string IMAGE = "Inspection.Image In Task:Image";

        // Task to re-run on Reprocess (re-evaluates the CURRENT image,
        // does NOT take a new one). Used by CallRunOnTask_Sync in ViewModel.Reprocess.
        public const string IMAGE_IN_TASK = "Inspection.Image In Task";

        // Gauge pass/fail. IMPORTANT: Gauge does NOT have a "Passed" port (only Line Find does).
        // Gauge uses "All In Tolerance" (confirmed in the mirror file ILineGaugeTool).
        public const string GAUGE_RESULT = "Inspection.Image In Task.Gauge:All In Tolerance";

        // Current measured distance: SINGLE-value sub-field of the Distance List (Real).
        public const string GAUGE_MEASURED = "Inspection.Image In Task.Gauge:Distance List.element";

        // The whole Distance List port (Real list) - used for the typed read in the ViewModel.
        public const string GAUGE_DISTANCE_LIST = "Inspection.Image In Task.Gauge:Distance List";

        // Gauge tolerance: the WHOLE Tolerance port (VisionTolerance).
        // PropertyTolerance edits this as one unit (minus/nominal/plus).
        public const string GAUGE_TOLERANCE = "Inspection.Image In Task.Gauge:Distance Tolerance";

        // Sub-fields (used ONLY if you want to bind PropertyNumeric to a single field
        // for DISPLAY - they cannot be written reliably individually).
        public const string GAUGE_NOMINAL = "Inspection.Image In Task.Gauge:Distance Tolerance.nominal";
        public const string GAUGE_MINUS = "Inspection.Image In Task.Gauge:Distance Tolerance.minus";
        public const string GAUGE_PLUS = "Inspection.Image In Task.Gauge:Distance Tolerance.plus";

        // ---------- BLOB ----------
        // Required Number Of Blobs: Range 1D (min/max number of blobs). The whole port
        // is edited as one unit (VisionRange), just like the Gauge tolerance.
        public const string BLOB_REQUIRED = "Inspection.Image In Task.Blob:Required Number Of Blobs";

        // Current number of blobs found (Integer) - for display.
        public const string BLOB_COUNT = "Inspection.Image In Task.Blob:Output Blob List.count";

        // Blob pass/fail. Blob DOES have a Passed port (unlike Gauge).
        public const string BLOB_RESULT = "Inspection.Image In Task.Blob:Passed";
    }
}