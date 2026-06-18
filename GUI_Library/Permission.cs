// ===================== Permission.cs =====================
using System;

namespace GUI_Library
{
    /// <summary>
    /// The concrete actions the app protects. Flags enum so a role can have
    /// several permissions combined. Check with HasFlag in the ViewModel.
    /// This is the different permission "tags" users have. 
    /// </summary>
    [Flags]
    public enum Permission
    {
        None = 0,
        CanOperate = 1 << 0,  // online / offline / trig
        CanEditGauge = 1 << 1,  // change gauge tolerance
        CanEditBlob = 1 << 2,  // change blob min/max
        CanSaveProgram = 1 << 3,  // save program to the camera
        CanViewAudit = 1 << 4   // view audit log in the app (future display)
    }
}