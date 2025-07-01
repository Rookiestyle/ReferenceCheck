using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KeePass;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.Resources;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Utility;
using PluginTools;
using PluginTranslation;

namespace ReferenceCheck
{
  public sealed class ReferenceCheckExt : Plugin
  {
    private IPluginHost m_host = null;
    private ToolStripMenuItem m_tsmiConfig = null;
    private ToolStripMenuItem m_tsmiFind = null;

    public override bool Initialize(IPluginHost host)
    {
      Terminate();
      if (host == null) return false;
      m_host = host;

      m_host.ColumnProviderPool.Add(new ReferenceCheckerColumnProvider());

      PluginTranslate.Init(this, Program.Translation.Properties.Iso6391Code);
      Tools.DefaultCaption = PluginTranslate.PluginName;
      Tools.PluginURL = "https://github.com/rookiestyle/referencecheck/";

      AddMenu();
      ToggleActive(Config.Active);

      Tools.OptionsFormShown += (o, e) => { Tools.AddPluginToOverview(GetType().Name); };

      return true;
    }

    private void AddMenu()
    {
      m_tsmiConfig = new ToolStripMenuItem();
      m_tsmiConfig.Text = string.Format(PluginTranslate.PluginActive, PluginTranslate.PluginName);
      m_tsmiConfig.Image = SmallIcon;
      m_tsmiConfig.Click += OnToggleActive;
      m_host.MainWindow.ToolsMenu.DropDownItems.Add(m_tsmiConfig);
      ImageOff = UIUtil.CreateGrayImage(SmallIcon);

      AddFindMenu();
    }


    private void AddFindMenu()
    {
      m_tsmiFind = new ToolStripMenuItem();
      m_tsmiFind.Text = PluginTranslate.PluginName + "...";
      m_tsmiFind.Image = SmallIcon;
      m_tsmiFind.Click += OnFindReferences;
      var tsmiAnchor = Tools.FindToolStripMenuItem(m_host.MainWindow.MainMenu.Items, "m_menuFindDupPasswords", true);
      if (tsmiAnchor != null)
      {
        var tsmiParent = tsmiAnchor.GetCurrentParent();
        tsmiParent.Items.Insert(tsmiParent.Items.IndexOf(tsmiAnchor), m_tsmiFind);
        if (tsmiParent is ToolStripDropDownMenu)
        {
          (tsmiParent as ToolStripDropDownMenu).Opening += ReferenceCheckExt_Opening;
        }
        return;
      }
      tsmiAnchor = Tools.FindToolStripMenuItem(m_host.MainWindow.MainMenu.Items, "m_menuFind", true);
      if (tsmiAnchor != null)
      {
        tsmiAnchor.DropDownItems.Add(m_tsmiFind);
        tsmiAnchor.DropDownOpening += ReferenceCheckExt_Opening;
      }
    }

    private void ReferenceCheckExt_Opening(object sender, EventArgs e)
    {
      m_tsmiFind.Enabled = m_host.Database.IsOpen && Config.Active;
      if (!m_tsmiFind.Enabled) return;
      var dbR = DB_Handler.Get_References(m_host.Database);
      m_tsmiFind.Enabled = dbR != null && (dbR.AllReferences.Count > 0 || dbR.AllBrokenReferences.Count > 0);
    }

    private void OnFindReferences(object sender, EventArgs e)
    {
      if (m_host.Database == null) return;
      if (!m_host.Database.IsOpen) return;
      var dbR = DB_Handler.Get_References(m_host.Database);
      if (dbR == null) return;
      if (dbR.AllReferences.Count == 0 && dbR.AllBrokenReferences.Count == 0) return;
      var dBroken = new Dictionary<PwEntry, List<string>>(dbR.AllBrokenReferences);

      var tc = new TabControl();

      Dictionary<PwEntry, List<PwEntry>> dReferencing = new Dictionary<PwEntry, List<PwEntry>>();
      Dictionary<PwEntry, List<PwEntry>> dReferences = new Dictionary<PwEntry, List<PwEntry>>();

      foreach (var f in dbR.AllReferences)
      {
        var l = DB_Handler.GetReferencingEntries(f.Entry);
        if (l.References.Count == 0 && !DB_Handler.HasBrokenReferences(f.Entry))
          continue;
        if (!dReferencing.ContainsKey(l.Entry)) dReferencing[l.Entry] = new List<PwEntry>();
        dReferencing[l.Entry].AddRange(l.References);
        foreach (var lR in l.References)
        {
          if (!dReferences.ContainsKey(lR)) dReferences[lR] = new List<PwEntry>();
          if (!dReferences[lR].Contains(f.Entry)) dReferences[lR].Add(f.Entry);
          dBroken.Remove(lR);
        }
      }
      ListView lv1 = null;
      ListView lv2 = null;
      foreach (var x in dReferencing)
        lv1 = AddReferencesTab(tc, PluginTranslate.Referencing, x.Value, x.Key);
      lv1 = AddReferencesTab(tc, PluginTranslate.Referencing, dBroken.Keys.ToList(), null);

      foreach (var x in dReferences)
        lv2 = AddReferencesTab(tc, PluginTranslate.ReferencedBy, x.Value, x.Key);
      if (lv1 == null && lv2 == null) return;
      var fo = new Form();
      fo.FormBorderStyle = FormBorderStyle.FixedDialog;
      fo.MaximizeBox = fo.MinimizeBox = false;
      fo.Width = m_host.MainWindow.Width / 2;
      fo.Height = m_host.MainWindow.Height / 2;

      tc.Dock = DockStyle.Fill;
      fo.Controls.Add(tc);
      var bCancel = new Button();
      bCancel.Click += (o, e1) => { fo.Close(); };
      fo.Controls.Add(bCancel);
      fo.CancelButton = bCancel;
      fo.Text = PluginTranslate.PluginName;
      fo.Shown += (o, e1) =>
      {
        if (lv1 != null) lv1.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        if (lv2 != null) lv2.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
      };
      fo.Icon = Resources.image_on;
      fo.StartPosition = FormStartPosition.CenterParent;
      fo.ShowInTaskbar = false;
      UIUtil.ShowDialogAndDestroy(fo);
      return;
    }

    private void OnToggleActive(object sender, EventArgs e)
    {
      Config.Active = !Config.Active;
      ToggleActive(Config.Active);
    }

    private void ToggleActive(bool bActive, bool bTerminating = false)
    {
      if (!bActive)
      {
        m_host.MainWindow.FileOpened -= DB_Handler.OnFileOpened;
        m_host.MainWindow.FileClosed -= DB_Handler.OnFileClosed;
        GlobalWindowManager.WindowAdded -= OnWindowAdded;
        GlobalWindowManager.WindowRemoved -= OnWindowClosed;
        PwEntry.EntryTouched -= OnEntryTouched;
        DB_Handler.RestoreDeleted -= OnShouldRestoreDeleted;
        m_host.MainWindow.UpdateUI(false, null, false, null, true, null, false);
        m_tsmiConfig.Image = ImageOff;
        m_tsmiConfig.Text = string.Format(PluginTranslate.PluginInactive, PluginTranslate.PluginName);
      }
      else
      {
        m_host.MainWindow.FileOpened += DB_Handler.OnFileOpened;
        m_host.MainWindow.FileClosed += DB_Handler.OnFileClosed;
        GlobalWindowManager.WindowAdded += OnWindowAdded;
        GlobalWindowManager.WindowRemoved += OnWindowClosed;
        PwEntry.EntryTouched += OnEntryTouched;
        DB_Handler.RestoreDeleted += OnShouldRestoreDeleted;
        m_tsmiConfig.Image = SmallIcon;
        m_tsmiConfig.Text = string.Format(PluginTranslate.PluginActive, PluginTranslate.PluginName);

        foreach (var db in m_host.MainWindow.DocumentManager.Documents)
        {
          if (!db.Database.IsOpen) continue;
          DB_Handler.OnFileClosed(null, new FileClosedEventArgs(db.Database.IOConnectionInfo, FileEventFlags.None));
          DB_Handler.OnFileOpened(null, new FileOpenedEventArgs(db.Database));
        }
      }
    }

    private void OnShouldRestoreDeleted(object sender, DB_RestoreDeleted e)
    {
      e.Restore = Tools.AskYesNo(string.Format(
          PluginTranslate.AskRestoreDeleted,
          PluginTranslate.PluginName,
          DB_Handler.GetDBName(e.DB),
          e.Entries.Count)) == DialogResult.Yes;
    }

    Dictionary<PwUuid, PwEntryForm> m_dPwEntryForms = new Dictionary<PwUuid, PwEntryForm>();
    private void OnWindowAdded(object sender, GwmWindowEventArgs e)
    {
      if (!Config.Active) return;
      PwEntryForm f = e.Form as PwEntryForm;
      if (f == null) return;
      m_dPwEntryForms[f.EntryRef.Uuid] = f;
      PwDatabase db = m_host.MainWindow.DocumentManager.SafeFindContainerOf(f.EntryRef);
      bool bAddTab = Config.ShowReferencedEntries && DB_Handler.HasReferences(f.EntryRef);
      bAddTab |= Config.ShowReferencedEntries && DB_Handler.HasBrokenReferences(f.EntryRef);
      bAddTab |= Config.ShowReferencingEntries && DB_Handler.IsReferenced(f.EntryRef);
      if (!bAddTab) return;

      f.Shown += OnShowEntryForm;
    }

    private void OnWindowClosed(object sender, GwmWindowEventArgs e)
    {
      PwEntryForm f = e.Form as PwEntryForm;
      if (f == null) return;
      m_dPwEntryForms.Remove(f.EntryRef.Uuid);
    }

    private void OnShowEntryForm(object sender, EventArgs e)
    {
      PwEntryForm f = sender as PwEntryForm;
      if (f == null) return;
      f.Shown -= OnShowEntryForm;
      TabControl tc = Tools.GetControl("m_tabMain", f) as TabControl;
      if (tc == null)
      {
        PluginDebug.AddError("Could not locate m_tabMain", 0);
        return;
      }
      TabPage tp = new TabPage(PluginTranslate.PluginName);
      tc.TabPages.Add(tp);
      tp.SuspendLayout();

      tc = new TabControl();
      tc.Dock = DockStyle.Fill;
      tp.Controls.Add(tc);
      if (Config.ShowReferencedEntries)
      {
        var lv = AddReferencesTab(tc,
                         PluginTranslate.Referencing,
                         DB_Handler.GetReferencedEntries(f.EntryRef), null, DB_Handler.GetBrokenReferences(f.EntryRef));
        if (lv != null)
          lv.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
      }
      if (Config.ShowReferencingEntries)
      {
        var lv = AddReferencesTab(tc,
                         PluginTranslate.ReferencedBy,
                         DB_Handler.GetReferencingEntries(f.EntryRef).References);
        if (lv != null)
          lv.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
      }
      //AddReferencingTab(tc, f.EntryRef);
      //AddReferencesTab(tc, f.EntryRef);

      tp.ResumeLayout();
    }

    private ListView AddReferencesTab(TabControl tc, string sTitle, List<PwEntry> lEntries, PwEntry pe = null, List<string> lBrokenRefs = null)
    {
      TabPage tp = null;
      ListView lv = null;
      for (int i = 0; i < tc.TabPages.Count; i++)
      {
        if (tc.TabPages[i].Text == sTitle)
        {
          tp = tc.TabPages[i];
          lv = Tools.GetControl("Rookiestyle_References_lv" + sTitle, tp) as ListView;
          break;
        }
      }
      if (lEntries.Count == 0 && (lBrokenRefs == null || lBrokenRefs.Count == 0)) return lv;
      if (tp == null)
      {
        tp = new TabPage(sTitle);
        lv = new ListView();
        lv.Name = "Rookiestyle_References_lv" + sTitle;
        tp.Controls.Add(lv);
        tc.TabPages.Add(tp);
      }
      List<ListViewGroup> lGroups = new List<ListViewGroup>();
      foreach (var g in lv.Groups)
        lGroups.Add(g as ListViewGroup);
      foreach (PwEntry er in lEntries)
      {
        ListViewGroup g = lGroups.Find(x => x.Tag == er.ParentGroup);
        if (g == null)
        {
          g = new ListViewGroup(GetGroupName(er.ParentGroup)) { Tag = er.ParentGroup };
          lGroups.Add(g);
          lv.Groups.Add(g);
        }
        ListViewItem lvi = new ListViewItem();
        lvi.Group = g;
        lvi.Text = er.Strings.ReadSafe(PwDefs.TitleField);
        lvi.Tag = er;
        var lviAdded = lv.Items.Add(lvi);
        if (pe != null)
        {
          var lvsi = new ListViewItem.ListViewSubItem();
          lvsi.Text = pe.Strings.ReadSafe(PwDefs.TitleField);
          lvsi.Tag = pe;
          if (DB_Handler.HasBrokenReferences(pe))
            lvsi.ForeColor = Color.Red;
          lviAdded.SubItems.Add(lvsi);
        }
        if (DB_Handler.HasBrokenReferences(er))
          lviAdded.SubItems[0].ForeColor = Color.Red;
        lvi.UseItemStyleForSubItems = false;
      }
      if (lBrokenRefs != null)
      {
        foreach (var sBrokenRef in lBrokenRefs)
        {
          ListViewGroup g = lGroups.Find(x => x.Tag == lBrokenRefs);
          if (g == null)
          {
            g = new ListViewGroup(string.Empty) { Tag = lBrokenRefs };
            lGroups.Add(g);
            lv.Groups.Add(g);
          }
          ListViewItem lvi = new ListViewItem();
          lvi.Group = g;
          lvi.Text = sBrokenRef;
          var lviAdded = lv.Items.Add(lvi);
          lviAdded.SubItems[0].ForeColor = Color.Red;
        }
      }
      lv.View = View.Details;
      lv.ShowGroups = true;
      lv.FullRowSelect = true;
      if (lv.Columns.Count == 0)
        lv.Columns.Add(KPRes.Entry);
      if (pe != null && lv.Columns.Count == 1)
      {
        if (sTitle == PluginTranslate.Referencing)
          lv.Columns.Add(PluginTranslate.Referencing);
        else
          lv.Columns.Add(PluginTranslate.ReferencedBy);
      }
      lv.MouseDoubleClick -= Lv_MouseDoubleClick;
      lv.MouseDoubleClick += Lv_MouseDoubleClick;
      lv.Dock = DockStyle.Fill;
      return lv;
    }

    private void Lv_MouseDoubleClick(object sender, MouseEventArgs e)
    {
      if (!(sender is ListView)) return;
      ListViewHitTestInfo lvHit = (sender as ListView).HitTest(e.Location);
      if (lvHit == null || (lvHit.Item as ListViewItem == null)) return;

      PwEntry pe = null;
      if (lvHit.SubItem != null && lvHit.SubItem.Tag is PwEntry)
        pe = lvHit.SubItem.Tag as PwEntry;
      if (pe == null)
        pe = lvHit.Item.Tag as PwEntry;
      if (pe == null) return;

      Action actOpenOtherEntry = new Action(() =>
      {
        PwEntryForm dlg = new PwEntryForm();
        dlg.InitEx(pe, PwEditMode.EditExistingEntry, m_host.Database, m_host.MainWindow.ClientIcons,
          false, false);
        dlg.MultipleValuesEntryContext = null;

        bool bOK = (dlg.ShowDialog(m_host.MainWindow) == DialogResult.OK);
        var bMod = (bOK && dlg.HasModifiedEntry);
        UIUtil.DestroyForm(dlg);

        if (!bOK) return;

        bool bUpdImg = m_host.Database.UINeedsIconUpdate; // Refreshing entries resets it
        m_host.MainWindow.RefreshEntriesList(); // Last access time
        m_host.MainWindow.UpdateUI(false, null, bUpdImg, null, false, null, bMod);

        if (Program.Config.Application.AutoSaveAfterEntryEdit && bMod)
          m_host.MainWindow.SaveDatabase(m_host.Database, null);
      });

      PwEntryForm peOtherForm = null;
      m_dPwEntryForms.TryGetValue(pe.Uuid, out peOtherForm);
      if (peOtherForm == null)
      {
        actOpenOtherEntry();
        return;
      }
      if (peOtherForm.Disposing || peOtherForm.IsDisposed)
      {
        m_dPwEntryForms.Remove(pe.Uuid);
        actOpenOtherEntry();
        return;
      }

      //The doubleclicked entry is already opened
      //Do nothing...
    }

    private string GetGroupName(PwGroup pg)
    {
      string sResult = string.Empty;
      if (pg == null) return sResult;
      PwGroup g = pg;
      sResult = pg.Name;
      while (g.ParentGroup != null)
      {
        g = g.ParentGroup;
        if (g.ParentGroup != null) sResult = g.Name + " - " + sResult;
      }
      return sResult;
    }

    private void OnEntryTouched(object sender, ObjectTouchedEventArgs e)
    {
      if (!Config.Active) return;
      if (!e.Modified) return;
      PwEntry pe = e.Object as PwEntry;
      if (pe == null) return;
      DB_Handler.UpdateReferences(pe);
    }

    public override void Terminate()
    {
      if (m_host == null) return;

      ToggleActive(false, true);

      m_host.MainWindow.ToolsMenu.DropDownItems.Remove(m_tsmiConfig);
      m_tsmiConfig.Dispose();

      if (m_tsmiFind.Owner != null)
      {
        m_tsmiFind.Owner.Items.Remove(m_tsmiFind);
      }
      m_tsmiFind.Dispose();

      PluginDebug.SaveOrShow();
      m_host = null;
    }

    public override string UpdateUrl
    {
      get { return "https://raw.githubusercontent.com/rookiestyle/referencecheck/master/version.info"; }
    }

    private Image _SmallIcon = null;
    private Image ImageOff = null;
    public override Image SmallIcon
    {
      get
      {
        if (_SmallIcon != null) return _SmallIcon;
        _SmallIcon = GfxUtil.ScaleImage(Resources.image_on.ToBitmap(), DpiUtil.ScaleIntX(32), DpiUtil.ScaleIntY(32));
        return _SmallIcon;
      }
    }
  }
}
