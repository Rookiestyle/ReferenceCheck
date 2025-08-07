using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using KeePass;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Utility;
using PluginTools;
using PluginTranslation;

namespace ReferenceCheck
{
  internal class DB_RestoreDeleted : EventArgs
  {
    internal PwDatabase DB { private set; get; }
    internal List<EntryReferences> Entries { private set; get; }
    internal bool Restore = true;

    public DB_RestoreDeleted(PwDatabase db, List<EntryReferences> lEntries)
    {
      DB = db;
      Entries = lEntries;
    }
  }

  internal class BrokenReference
  {
    internal PwEntry Entry;
    internal List<string> BrokenReferences;
  }

  internal static class DB_Handler
  {
    private static Timer m_tTimer = new Timer();

    private static Dictionary<string, DB_References> m_dDB = new Dictionary<string, DB_References>();

    internal static EventHandler<DB_RestoreDeleted> RestoreDeleted;

    private static List<PwEntry> EmptyEntryList = new List<PwEntry>();

    private static List<string> m_lEmptyList = new List<string>();

    static DB_Handler()
    {
      m_tTimer.Tick += CheckDeleted;
      m_tTimer.Interval = 1000;
      m_tTimer.Start();
    }

    internal static void OnFileOpened(object sender, FileOpenedEventArgs e)
    {
      if (!Config.Active) return;
      DB_References db = new DB_References(e.Database);
      db.RestoreDeleted += RestoreDeleted;
      m_dDB[e.Database.IOConnectionInfo.Path] = db;
      db.FillReferences();
      if (Program.MainForm.ActiveDatabase != e.Database) return;
      Program.MainForm.UpdateUI(false, null, false, null, true, null, false);
    }

    internal static void OnFileClosed(object sender, FileClosedEventArgs e)
    {
      if (e.IOConnectionInfo == null || e.IOConnectionInfo.Path == null) return;
      if (m_dDB.ContainsKey(e.IOConnectionInfo.Path) && (m_dDB[e.IOConnectionInfo.Path] != null))
      {
        m_dDB[e.IOConnectionInfo.Path].RestoreDeleted -= RestoreDeleted;
      }
      m_dDB[e.IOConnectionInfo.Path] = null;
      m_dDB.Remove(e.IOConnectionInfo.Path);
    }

    internal static DB_References GetDBFromEntry(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return null;

      if (!m_dDB.ContainsKey(db)) return null;

      return m_dDB[db];
    }

    internal static EntryReferences GetReferencingEntries(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return DB_References.NoReferences;
      if (!m_dDB.ContainsKey(db)) return DB_References.NoReferences;
      return m_dDB[db].GetReferencingEntries(pe.Uuid);
    }

    public static string GetDB(PwDatabase db)
    {
      return db.IsOpen ? db.IOConnectionInfo.Path : string.Empty;
    }

    private static string GetDB(PwEntry pe)
    {
      // SafeFindContainerOf / FindContainerOf return the active databass if no db is found / is too slow
      // Only use first part of FindContainerOf
      if (pe == null) return string.Empty;

      PwGroup pg = pe.ParentGroup;
      if (pg == null) return string.Empty;
      {
        while (pg.ParentGroup != null) { pg = pg.ParentGroup; }

        foreach (PwDocument ds in KeePass.Program.MainForm.DocumentManager.Documents)
        {
          PwDatabase pd = ds.Database;
          if ((pd == null) || !pd.IsOpen) continue;

          if (object.ReferenceEquals(pd.RootGroup, pg))
            return pd.IOConnectionInfo.Path;
        }
      }
      return string.Empty;
    }

    internal static bool IsReferenced(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return false;
      return m_dDB[db].IsReferenced(pe.Uuid);
    }

    internal static bool HasReferences(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return false;
      return m_dDB[db].HasReferences(pe.Uuid);
    }

    internal static void UpdateReferences(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return;
      m_dDB[db].UpdateReferences(pe);
    }

    private static bool m_bChecking = false;
    private static void CheckDeleted(object sender, EventArgs e)
    {
      if (m_bChecking) return;
      m_bChecking = true;
      foreach (var db in m_dDB)
      {
        if (db.Value != null) db.Value.CheckDeleted();
      }
      m_bChecking = false;
    }

    internal static string GetDBName(PwDatabase m_db)
    {
      if (string.IsNullOrEmpty(m_db.Name)) return UrlUtil.GetFileName(m_db.IOConnectionInfo.Path);
      return m_db.Name;
    }

    internal static List<PwEntry> GetReferencedEntries(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return EmptyEntryList;
      if (!m_dDB.ContainsKey(db)) return EmptyEntryList;
      return m_dDB[db].GetReferencedEntries(pe.Uuid);
    }

    internal static DB_References Get_References(PwDatabase db)
    {
      var sDB = DB_Handler.GetDB(db);
      if (m_dDB.ContainsKey(sDB)) return m_dDB[sDB];
      return null;
    }

    internal static bool HasBrokenReferences(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return false;
      if (!m_dDB.ContainsKey(db)) return false;
      return m_dDB[db].HasBrokenReferences(pe);
    }

    internal static List<string> GetBrokenReferences(PwEntry pe)
    {
      string db = GetDB(pe);
      if (string.IsNullOrEmpty(db)) return m_lEmptyList;
      return m_dDB[db].GetBrokenReferences(pe);
    }
  }

  internal class DB_References
  {
    internal EventHandler<DB_RestoreDeleted> RestoreDeleted;

    private List<EntryReferences> m_lReferences = new List<EntryReferences>();
    private Dictionary<PwUuid, BrokenReference> m_dBrokenReferences = new Dictionary<PwUuid, BrokenReference>();
    private PwDatabase m_db = null;
    private string m_sDBPath = string.Empty;
    private List<string> m_lEmptyList = new List<string>();

    private int m_iDelObjCount = 0;

    public List<EntryReferences>  AllReferences { get { return m_lReferences; } }

    public Dictionary<PwEntry, List<string>> AllBrokenReferences 
    { 
      get 
      {
        return m_dBrokenReferences.ToDictionary(x=>x.Value.Entry, x=>x.Value.BrokenReferences);
      } 
    }

    public static readonly EntryReferences NoReferences = new EntryReferences(null);
    internal DB_References(PwDatabase db)
    {
      m_db = db;
      if ((m_db != null) && (m_db.IsOpen)) m_sDBPath = m_db.IOConnectionInfo.Path;
    }

    public void FillReferences()
    {
      PwObjectList<PwEntry> lEntries = m_db.RootGroup.GetEntries(true);
      foreach (PwEntry pe in lEntries)
      {
        FillReferencesSingle(pe);
      }
    }

    private void FillReferencesSingle(PwEntry pe)
    {
      if (pe == null) return;
      List<string> lRefs = GetReferenceStrings(pe);
      List<string> lBrokenRefs = null;
      List<PwEntry> lRefEntries = GetReferencedEntries(lRefs, pe, out lBrokenRefs);
      foreach (PwEntry peRef in lRefEntries)
      {
        EntryReferences er = m_lReferences.Find(x => x.Entry.Uuid.Equals(peRef.Uuid));
        if (er == null)
        {
          er = new EntryReferences(peRef);
          m_lReferences.Add(er);
        }
        if (!er.References.Contains(pe)) er.References.Add(pe);
      }

      //m_dBrokenReferences = m_dBrokenReferences.Where(x => x.Key.Uuid != pe.Uuid).ToDictionary(x => x.Key, x => x.Value);
      m_dBrokenReferences.Remove(pe.Uuid);

      if (lBrokenRefs.Count > 0)
        m_dBrokenReferences[pe.Uuid] = new BrokenReference() { Entry = pe, BrokenReferences = lBrokenRefs };
    }

    internal void CheckDeleted()
    {
      if (!Config.Active) return;
      if (m_iDelObjCount == m_db.DeletedObjects.UCount) return;
      lock (m_db.DeletedObjects)
      {
        m_iDelObjCount = (int)m_db.DeletedObjects.UCount;
        List<EntryReferences> lRestore = new List<EntryReferences>();
        List<PwDeletedObject> lDeleted = m_db.DeletedObjects.CloneShallowToList();
        foreach (EntryReferences er in m_lReferences)
        {
          if (lDeleted.Find(x => x.Uuid == er.Entry.Uuid) != null) lRestore.Add(er);
        }
        if (lRestore.Count == 0) return;

        DB_RestoreDeleted rd = new DB_RestoreDeleted(m_db, lRestore);
        if (RestoreDeleted != null) RestoreDeleted(this, rd);
        if (!rd.Restore)
        {
          //Remove entries where the user decided to NOT restore
          foreach (EntryReferences er in lRestore) m_lReferences.Remove(er);
          return;
        }
        bool bRestoredGroups = false;
        foreach (EntryReferences er in lRestore)
        {
          m_db.DeletedObjects.Remove(lDeleted.Find(x => x.Uuid == er.Entry.Uuid));
          bool bRestoreInParentGroup = !IsRecycled(er.Entry.ParentGroup); //Restore in rootgroup if entry's parentgroup is in recycle bin
          if (bRestoreInParentGroup)
          {
            List<PwGroup> lGroups = new List<PwGroup>();
            PwGroup pg = er.Entry.ParentGroup;
            while (pg != null)
            {
              if (m_db.DeletedObjects.FirstOrDefault(x => x.Uuid == pg.Uuid) == null) break;
              lGroups.Insert(0, pg);
              pg = pg.ParentGroup;
            }
            bRestoredGroups |= lGroups.Count > 0;
            foreach (PwGroup pgRestore in lGroups)
            {
              pgRestore.ParentGroup.AddGroup(pgRestore, true);
              m_db.DeletedObjects.Remove(lDeleted.Find(x => x.Uuid == pgRestore.Uuid));
            }
            er.Entry.ParentGroup.AddEntry(er.Entry, true);
          }
          else
          {
            PwGroup pg = m_db.RootGroup.GetGroups(true).FirstOrDefault(x => x.CustomData.Exists(Config.RestoreGroup));
            if (pg == null)
            {
              pg = new PwGroup(true, true);
              pg.Name = string.Format(PluginTranslate.RestoreGroupName, PluginTranslate.PluginName);
              pg.CustomData.Set(Config.RestoreGroup, true.ToString());
              m_db.RootGroup.AddGroup(pg, true, true);
              bRestoredGroups = true;
            }
            if (pg == null) pg = m_db.RootGroup;
            pg.AddEntry(er.Entry, true);
          }
        }
        if (Program.MainForm.ActiveDatabase == m_db) Program.MainForm.UpdateUI(false, null, bRestoredGroups, null, true, null, false);
      }
    }

    private bool IsRecycled(PwGroup pg)
    {
      if (!m_db.RecycleBinEnabled) return false;
      PwGroup pgRecycleBin = m_db.RootGroup.FindGroup(m_db.RecycleBinUuid, true);
      if (pgRecycleBin == null)
      {
        //If the recycle bin has been deleted, it's no longer contained in the rootgroup
        //Check deleted objects as well
        return m_db.DeletedObjects.FirstOrDefault(x => x.Uuid == m_db.RecycleBinUuid) != null;
      }
      if (pgRecycleBin == null) return false;
      return pg.Uuid.Equals(pgRecycleBin.Uuid) || pg.IsContainedIn(pgRecycleBin);
    }

    internal void UpdateReferences(PwEntry pe)
    {
      for (int i = m_lReferences.Count -1; i >= 0; i--)
      {
        EntryReferences er = m_lReferences[i];
        for (int j = er.References.Count - 1; j >= 0; j--)
        {
          if (er.References[j].Uuid != pe.Uuid) continue;
          er.References.RemoveAt(j);
        }
        if (er.References.Count == 0) m_lReferences.RemoveAt(i);
      }
      FillReferencesSingle(pe);
    }

    private List<PwEntry> GetReferencedEntries(List<string> lRefs, PwEntry pe, out List<string> lBrokenRefs)
    {
      List<PwEntry> lEntries = new List<PwEntry>();
      lBrokenRefs = new List<string>();
      if (lRefs == null) return lEntries;
      foreach (string sRef in lRefs)
      {
        char cScan;
        char cWanted;
        //string s = KeePass.Util.Spr.SprEngine.FillRefPlaceholdersPub(sRef,
        //    new KeePass.Util.Spr.SprContext(pe, m_db, KeePass.Util.Spr.SprCompileFlags.All), 1);
        var scfFlags = KeePass.Util.Spr.SprCompileFlags.NonActive;
        scfFlags ^= KeePass.Util.Spr.SprCompileFlags.References;
        var sRefToUse = KeePass.Util.Spr.SprEngine.Compile(sRef, new KeePass.Util.Spr.SprContext(pe, m_db, scfFlags));
        PwEntry peRef = KeePass.Util.Spr.SprEngine.FindRefTarget(sRefToUse, new KeePass.Util.Spr.SprContext(pe, m_db, KeePass.Util.Spr.SprCompileFlags.All), out cScan, out cWanted);
        if (peRef == null)
        {
          if (!lBrokenRefs.Contains(sRef)) lBrokenRefs.Add(sRef);
          continue;
        }
        if (!lEntries.Contains(peRef)) lEntries.Add(peRef);
      }
      return lEntries;
    }

    private List<string> GetReferenceStrings(PwEntry pe)
    {
      //Use array of char to not break the ProtectedString
      char[] aRefStart = @"{REF:".ToCharArray();
      char cRefEnd = '}';
      int iOpeningBrackets = 0;
      List<string> lResult = new List<string>();
      foreach (var ps in pe.Strings)
      {
        char[] cString = ps.Value.ReadChars();
        for (int i = 0; i < cString.Length - aRefStart.Length; i++)
        {
          if (char.ToLowerInvariant(cString[i]) == char.ToLower(aRefStart[0]))
          {
            bool bFound = true;
            for (int j = 1; j < aRefStart.Length; j++)
            {
              bFound &= char.ToLowerInvariant(cString[i + j]) == char.ToLower(aRefStart[j]);
              if (!bFound) break;
            }
            if (bFound)
            {
              int iStart = i;
              int iEnd = -1;
              string sRef = string.Empty;
              for (int j = i; j < cString.Length; j++)
              {
                sRef += cString[j];
                if (cString[j] == '{') iOpeningBrackets++; 
                if (cString[j] == cRefEnd)
                {
                  iOpeningBrackets--;
                  if (iOpeningBrackets == 0)
                  {
                    iEnd = j;
                    break;
                  }
                }
              }
              if (iEnd > -1) lResult.Add(sRef);
            }
          }
        }
        MemUtil.ZeroArray(cString);
      }
      return lResult;
    }

    internal EntryReferences GetReferencingEntries(PwUuid uuid)
    {
      var r = m_lReferences.Find(x => x.Entry.Uuid == uuid);
      if (r == null) return NoReferences;
      return r;
    }

    internal bool IsReferenced(PwUuid uuid)
    {
      var r = m_lReferences.FirstOrDefault(x => x.Entry.Uuid == uuid);
      if (r == null) return false;
      if (r.References.Count == 0)
      {
        m_lReferences.Remove(r);
        return false;
      }
      return true;
      //return m_lReferences.Exists(x => (x.Entry.Uuid == uuid) && (x.References.Count > 0));
    }

    internal bool HasReferences(PwUuid uuid)
    {
      return m_lReferences.Exists(x => x.References.Exists(y => y.Uuid == uuid));
    }

    internal bool HasBrokenReferences(PwEntry pe)
    {
      //return m_dBrokenReferences.Count(x => x.Key.Uuid.Equals(pe.Uuid)) > 0;
      return m_dBrokenReferences.ContainsKey(pe.Uuid);
    }

    internal List<string> GetBrokenReferences(PwEntry pe)
    {
      if (!HasBrokenReferences(pe)) return m_lEmptyList;
      return m_dBrokenReferences[pe.Uuid].BrokenReferences;
    }

    internal List<PwEntry> GetReferencedEntries(PwUuid uuid)
    {
      List<PwEntry> lResult = new List<PwEntry>();
      List<EntryReferences> lER = m_lReferences.FindAll(x => x.References.Exists(y => y.Uuid == uuid));
      foreach (EntryReferences er in lER) lResult.Add(er.Entry);
      return lResult;
    }
  }

  internal class EntryReferences
  {
    public PwEntry Entry;
    public List<PwEntry> References = new List<PwEntry>();

    public EntryReferences(PwEntry pe)
    {
      Entry = pe;
    }
  }
}
