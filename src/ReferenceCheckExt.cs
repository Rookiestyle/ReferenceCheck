using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Resources;
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

    private void OnWindowAdded(object sender, GwmWindowEventArgs e)
    {
      if (!Config.Active) return;
      PwEntryForm f = e.Form as PwEntryForm;
      if (f == null) return;
      PwDatabase db = m_host.MainWindow.DocumentManager.SafeFindContainerOf(f.EntryRef);
      bool bAddTab = Config.ShowReferencedEntries && DB_Handler.HasReferences(f.EntryRef);
      bAddTab |= Config.ShowReferencingEntries && DB_Handler.IsReferenced(f.EntryRef);
      if (!bAddTab) return;

      f.Shown += OnShowEntryForm;
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
        AddReferencesTab(tc,
                         PluginTranslate.Referencing,
                         DB_Handler.GetReferencedEntries(f.EntryRef));
      }
      if (Config.ShowReferencingEntries)
      {
        AddReferencesTab(tc,
                         PluginTranslate.ReferencedBy,
                         DB_Handler.GetReferencingEntries(f.EntryRef).References);
      }
      //AddReferencingTab(tc, f.EntryRef);
      //AddReferencesTab(tc, f.EntryRef);

      tp.ResumeLayout();
    }

    private void AddReferencesTab(TabControl tc, string sTitle, List<PwEntry> lEntries)
    {
      if (lEntries.Count == 0) return;
      TabPage tp = new TabPage(sTitle);
      ListView lv = new ListView();
      List<ListViewGroup> lGroups = new List<ListViewGroup>();
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
        lv.Items.Add(lvi);
      }
      lv.View = View.Details;
      lv.ShowGroups = true;
      lv.FullRowSelect = true;
      lv.Columns.Add(KPRes.Title);
      lv.Dock = DockStyle.Fill;
      tp.Controls.Add(lv);
      lv.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
      tc.TabPages.Add(tp);
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
