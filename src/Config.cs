using KeePass.App.Configuration;

namespace ReferenceCheck
{
  internal static class Config
  {
    private const string ConfigPrefix = "ReferenceChecker.";
    //private static string m_ConfigShowReferencingEntries = ConfigPrefix + "ShowReferencingEntries";
    //private static string m_ConfigShowReferencedEntries = ConfigPrefix + "ShowReferencedEntries";
    //private static string m_ConfigAutoRestore = ConfigPrefix + "AutoRestore";
    private static string m_ConfigActive = ConfigPrefix + "Active";

    private static AceCustomConfig m_conf = KeePass.Program.Config.CustomConfig;

    internal static string RestoreGroup = ConfigPrefix + "RestoreGroup";

    internal static bool Active
    {
      get { return m_conf.GetBool(m_ConfigActive, true); }
      set { m_conf.SetBool(m_ConfigActive, value); }
    }

    internal static readonly bool ShowReferencingEntries = true;
    internal static readonly bool ShowReferencedEntries = true;

    /*
    internal static bool ShowReferencingEntries
    {
      get { return m_conf.GetBool(m_ConfigShowReferencingEntries, true); }
      set { m_conf.SetBool(m_ConfigShowReferencingEntries, value); }
    }

    internal static bool ShowReferencedEntries
    {
      get { return m_conf.GetBool(m_ConfigShowReferencedEntries, true); }
      set { m_conf.SetBool(m_ConfigShowReferencedEntries, value); }
    }

    internal static bool AutoRestore
    {
      get { return m_conf.GetBool(m_ConfigAutoRestore, true); }
      set { m_conf.SetBool(m_ConfigAutoRestore, value); }
    }
    */
  }
}
