using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePass.UI;
using KeePassLib;

namespace ReferenceCheck
{
  internal class ReferenceCheckerColumnProvider : ColumnProvider
  {
    public const string RCColumn = "RefCheck";

    private readonly string[] ColumnName = new[] { RCColumn };

    public override string[] ColumnNames { get { return ColumnName; } }

    public override string GetCellData(string strColumnName, PwEntry pe)
    {
      if (strColumnName == null) return string.Empty;
      if (pe == null) return string.Empty;
      if (!Config.Active) return "?";
      var iReferenced = DB_Handler.GetReferencedEntries(pe).Count;
      var oReferencing = DB_Handler.GetReferencingEntries(pe);
      var iReferecning = oReferencing == null ? 0 : oReferencing.References.Count;
      if (iReferenced == 0 && iReferecning == 0) return string.Empty;
      return iReferenced.ToString() + " / " + iReferecning.ToString();
    }

    public override bool SupportsCellAction(string strColumnName)
    {
      return false;
    }
  }
}
