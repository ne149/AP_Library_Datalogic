// ===================== Permission.cs =====================
using System;

namespace GUI_Library
{
    /// <summary>
    /// De konkrete handlinger appen beskytter. Flag-enum saa en rolle kan have
    /// flere rettigheder kombineret. Tjek med HasFlag i ViewModel.
    /// </summary>
    [Flags]
    public enum Permission
    {
        None = 0,
        CanOperate = 1 << 0,  // online / offline / trig
        CanEditGauge = 1 << 1,  // aendre gauge-tolerance
        CanEditBlob = 1 << 2,  // aendre blob min/max
        CanSaveProgram = 1 << 3,  // gem program til kameraet
        CanViewAudit = 1 << 4   // se audit-log i appen (fremtidig visning)
    }
}