﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
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

  internal static class DB_Handler
  {
    private static Timer m_tTimer = new Timer();

    private static Dictionary<string, DB_References> m_dDB = new Dictionary<string, DB_References>();

    internal static EventHandler<DB_RestoreDeleted> RestoreDeleted;

    private static List<PwEntry> EmptyEntryList = new List<PwEntry>();

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
  }

  internal class DB_References
  {
    internal EventHandler<DB_RestoreDeleted> RestoreDeleted;

    private List<EntryReferences> m_lReferences = new List<EntryReferences>();
    private PwDatabase m_db = null;
    private string m_sDBPath = string.Empty;

    private int m_iDelObjCount = 0;

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
      List<PwEntry> lRefEntries = GetReferencedEntries(lRefs, pe);
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
      foreach (EntryReferences er in m_lReferences)
      {
        for (int i = er.References.Count - 1; i >= 0; i--)
        {
          if (er.References[i].Uuid == pe.Uuid) er.References.RemoveAt(i);
        }
      }
      FillReferencesSingle(pe);
    }

    private List<PwEntry> GetReferencedEntries(List<string> lRefs, PwEntry pe)
    {
      List<PwEntry> lEntries = new List<PwEntry>();
      if (lRefs == null) return lEntries;
      foreach (string sRef in lRefs)
      {
        char cScan;
        char cWanted;
        PwEntry peRef = KeePass.Util.Spr.SprEngine.FindRefTarget(sRef, new KeePass.Util.Spr.SprContext(pe, m_db, KeePass.Util.Spr.SprCompileFlags.References), out cScan, out cWanted);
        if (peRef == null) continue;
        if (!lEntries.Contains(peRef)) lEntries.Add(peRef);
      }
      return lEntries;
    }

    private List<string> GetReferenceStrings(PwEntry pe)
    {
      //Use array of char to not break the ProtectedString
      char[] aRefStart = @"{REF:".ToCharArray();
      char cRefEnd = '}';
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
                if (cString[j] == cRefEnd)
                {
                  iEnd = j;
                  break;
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
      return m_lReferences.Exists(x => (x.Entry.Uuid == uuid) && (x.References.Count > 0));
    }

    internal bool HasReferences(PwUuid uuid)
    {
      return m_lReferences.Exists(x => x.References.Exists(y => y.Uuid == uuid));
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
