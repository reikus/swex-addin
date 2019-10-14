﻿using CodeStack.SwEx.AddIn.Base;
using CodeStack.SwEx.AddIn.Core;
using CodeStack.SwEx.AddIn.Delegates;
using CodeStack.SwEx.AddIn.Enums;
using CodeStack.SwEx.AddIn.Exceptions;
using CodeStack.SwEx.AddIn.Icons;
using CodeStack.SwEx.Common.Base;
using CodeStack.SwEx.Common.Diagnostics;
using CodeStack.SwEx.Common.Icons;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeStack.SwEx.AddIn.Modules
{
    internal class CommandManagerModule : IDisposable
    {
        private class TabCommandInfo
        {
            internal swDocumentTypes_e DocType { get; private set; }
            internal int CmdId { get; private set; }
            internal swCommandTabButtonTextDisplay_e TextType { get; private set; }

            internal TabCommandInfo(swDocumentTypes_e docType, int cmdId,
                swCommandTabButtonTextDisplay_e textType)
            {
                DocType = docType;
                CmdId = cmdId;
                TextType = textType;
            }
        }

        private const string SUB_GROUP_SEPARATOR = "\\";

        private readonly Dictionary<ICommandGroupSpec, CommandGroup> m_CommandGroups;
        private readonly Dictionary<string, ICommandSpec> m_Commands;
        private readonly ILogger m_Logger;
        private readonly ISldWorks m_App;

        internal ICommandManager CmdMgr { get; private set; }

        internal CommandManagerModule(ISldWorks app, int addinCookie, ILogger logger)
        {
            m_App = app;

            CmdMgr = app.GetCommandManager(addinCookie);

            m_Logger = logger;
            m_Commands = new Dictionary<string, ICommandSpec>();
            m_CommandGroups = new Dictionary<ICommandGroupSpec, CommandGroup>();
        }

        internal void HandleCommandClick(string cmdId)
        {
            m_Logger.Log($"Command clicked: {cmdId}");

            ICommandSpec cmd;

            if (m_Commands.TryGetValue(cmdId, out cmd))
            {
                cmd.OnClick();
            }
            else
            {
                Debug.Assert(false, "All callbacks must be registered");
            }
        }

        internal int HandleCommandEnable(string cmdId)
        {
            ICommandSpec cmd;

            if (m_Commands.TryGetValue(cmdId, out cmd))
            {
                return (int)cmd.OnEnable();
            }
            else
            {
                Debug.Assert(false, "All callbacks must be registered");
            }

            return (int)CommandItemEnableState_e.DeselectDisable;
        }

        internal CommandGroup AddCommandGroupOrContextMenu<TCmdEnum>(Action<TCmdEnum> callback,
            bool isContextMenu, swSelectType_e contextMenuSelectType = swSelectType_e.swSelEVERYTHING,
            EnableMethodDelegate<TCmdEnum> enable = null)
            where TCmdEnum : IComparable, IFormattable, IConvertible
        {
            return AddCommandGroupOrContextMenu(
                new EnumCommandGroupSpec<TCmdEnum>(m_App, callback, enable, GetNextAvailableGroupId(), m_CommandGroups.Keys), isContextMenu,
                contextMenuSelectType);
        }

        internal CommandGroup AddCommandGroupOrContextMenu(ICommandGroupSpec cmdBar,
            bool isContextMenu, swSelectType_e contextMenuSelectType)
        {
            m_Logger.Log($"Creating command group: {cmdBar.Id}");

            if (m_CommandGroups.Keys.FirstOrDefault(g => g.Id == cmdBar.Id) != null)
            {
                throw new GroupIdAlreadyExistsException(cmdBar);
            }

            var title = GetMenuPath(cmdBar);

            var cmdGroup = CreateCommandGroup(cmdBar.Id, title, cmdBar.Tooltip,
                cmdBar.Commands.Select(c => c.UserId).ToArray(), isContextMenu,
                contextMenuSelectType);

            m_CommandGroups.Add(cmdBar, cmdGroup);

            using (var iconsConv = new IconsConverter())
            {
                CreateIcons(cmdGroup, cmdBar, iconsConv);

                var createdCmds = CreateCommandItems(cmdGroup, cmdBar.Id, cmdBar.Commands);

                var tabGroup = GetRootCommandGroup(cmdBar);

                try
                {
                    CreateCommandTabBox(tabGroup, createdCmds);
                }
                catch (Exception ex)
                {
                    m_Logger.Log(ex);
                    //not critical error - continue operation
                }
            }

            return cmdGroup;
        }

        private int GetNextAvailableGroupId()
        {
            if (m_CommandGroups.Any())
            {
                return m_CommandGroups.Keys.Max(g => g.Id) + 1;
            }
            else
            {
                return 0;
            }
        }

        private CommandGroup GetRootCommandGroup(ICommandGroupSpec cmdBar)
        {
            var root = cmdBar;

            while (root.Parent != null)
            {
                root = root.Parent;
            }

            return m_CommandGroups[root];
        }

        private string GetMenuPath(ICommandGroupSpec cmdBar)
        {
            var title = new StringBuilder();

            var parent = cmdBar.Parent;

            while (parent != null)
            {
                title.Insert(0, parent.Title + SUB_GROUP_SEPARATOR);
                parent = parent.Parent;
            }

            title.Append(cmdBar.Title);

            return title.ToString();
        }

        private CommandGroup CreateCommandGroup(int groupId, string title, string toolTip,
            int[] knownCmdIDs, bool isContextMenu, swSelectType_e contextMenuSelectType)
        {
            int cmdGroupErr = 0;

            object registryIDs;

            var isChanged = true;

            if (CmdMgr.GetGroupDataFromRegistry(groupId, out registryIDs))
            {
                isChanged = !CompareIDs(registryIDs as int[], knownCmdIDs);
            }

            m_Logger.Log($"Command ids changed: {isChanged}");

            CommandGroup cmdGroup;

            if (isContextMenu)
            {
                cmdGroup = CmdMgr.AddContextMenu(groupId, title);
                cmdGroup.SelectType = (int)contextMenuSelectType;
            }
            else
            {
                cmdGroup = CmdMgr.CreateCommandGroup2(groupId, title, toolTip,
                    toolTip, -1, isChanged, ref cmdGroupErr);

                m_Logger.Log($"Command group creation result: {(swCreateCommandGroupErrors)cmdGroupErr}");

                Debug.Assert(cmdGroupErr == (int)swCreateCommandGroupErrors.swCreateCommandGroup_Success);
            }

            return cmdGroup;
        }

        private void CreateIcons(CommandGroup cmdGroup, ICommandGroupSpec cmdBar, IIconsConverter iconsConv)
        {
            var mainIcon = cmdBar.Icon;

            CommandGroupIcon[] iconList = null;

            if (cmdBar.Commands != null)
            {
                iconList = cmdBar.Commands.Select(c => c.Icon).ToArray();
            }

            //NOTE: if commands are not used, main icon will fail if toolbar commands image list is not specified, so it is required to specify it explicitly

            if (m_App.SupportsHighResIcons(SldWorksExtension.HighResIconsScope_e.CommandManager))
            {
                var iconsList = iconsConv.ConvertIcon(mainIcon, true);
                cmdGroup.MainIconList = iconsList;

                if (iconList != null && iconList.Any())
                {
                    cmdGroup.IconList = iconsConv.ConvertIconsGroup(iconList, true);
                }
                else
                {
                    cmdGroup.IconList = iconsList;
                }
            }
            else
            {
                var mainIconPath = iconsConv.ConvertIcon(mainIcon, false);

                var smallIcon = mainIconPath[0];
                var largeIcon = mainIconPath[1];

                cmdGroup.SmallMainIcon = smallIcon;
                cmdGroup.LargeMainIcon = largeIcon;

                if (iconList != null && iconList.Any())
                {
                    var iconListPath = iconsConv.ConvertIconsGroup(iconList, true);
                    var smallIconList = iconListPath[0];
                    var largeIconList = iconListPath[1];

                    cmdGroup.SmallIconList = smallIconList;
                    cmdGroup.LargeIconList = largeIconList;
                }
                else
                {
                    cmdGroup.SmallIconList = smallIcon;
                    cmdGroup.LargeIconList = largeIcon;
                }
            }
        }

        private Dictionary<ICommandSpec, int> CreateCommandItems(CommandGroup cmdGroup, int groupId, ICommandSpec[] cmds)
        {
            var createdCmds = new Dictionary<ICommandSpec, int>();

            var callbackMethodName = nameof(SwAddInEx.OnCommandClick);
            var enableMethodName = nameof(SwAddInEx.OnCommandEnable);

            for (int i = 0; i < cmds.Length; i++)
            {
                var cmd = cmds[i];

                swCommandItemType_e menuToolbarOpts = 0;

                if (cmd.HasMenu)
                {
                    menuToolbarOpts |= swCommandItemType_e.swMenuItem;
                }

                if (cmd.HasToolbar)
                {
                    menuToolbarOpts |= swCommandItemType_e.swToolbarItem;
                }

                if (menuToolbarOpts == 0)
                {
                    throw new InvalidMenuToolbarOptionsException(cmd);
                }

                var cmdName = $"{groupId}.{cmd.UserId}";

                m_Commands.Add(cmdName, cmd);

                var callbackFunc = $"{callbackMethodName}({cmdName})";
                var enableFunc = $"{enableMethodName}({cmdName})";

                if (cmd.HasSpacer)
                {
                    cmdGroup.AddSpacer2(-1, (int)menuToolbarOpts);
                }

                var cmdIndex = cmdGroup.AddCommandItem2(cmd.Title, -1, cmd.Tooltip,
                    cmd.Title, i, callbackFunc, enableFunc, cmd.UserId,
                    (int)menuToolbarOpts);

                createdCmds.Add(cmd, cmdIndex);

                m_Logger.Log($"Created command {cmdIndex} for {cmd}");
            }

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = true;
            cmdGroup.Activate();

            return createdCmds.ToDictionary(p => p.Key, p => cmdGroup.CommandID[p.Value]);
        }

        private void CreateCommandTabBox(CommandGroup cmdGroup, Dictionary<ICommandSpec, int> commands)
        {
            m_Logger.Log($"Creating command tab box");

            var tabCommands = new List<TabCommandInfo>();

            foreach (var cmdData in commands)
            {
                var cmd = cmdData.Key;
                var cmdId = cmdData.Value;

                if (cmd.HasTabBox)
                {
                    var docTypes = new List<swDocumentTypes_e>();

                    if (cmd.SupportedWorkspace.HasFlag(swWorkspaceTypes_e.Part))
                    {
                        docTypes.Add(swDocumentTypes_e.swDocPART);
                    }

                    if (cmd.SupportedWorkspace.HasFlag(swWorkspaceTypes_e.Assembly))
                    {
                        docTypes.Add(swDocumentTypes_e.swDocASSEMBLY);
                    }

                    if (cmd.SupportedWorkspace.HasFlag(swWorkspaceTypes_e.Drawing))
                    {
                        docTypes.Add(swDocumentTypes_e.swDocDRAWING);
                    }

                    tabCommands.AddRange(docTypes.Select(
                        t => new TabCommandInfo(
                            t, cmdId, cmd.TabBoxStyle)));
                }
            }

            foreach (var cmdGrp in tabCommands.GroupBy(c => c.DocType))
            {
                var docType = cmdGrp.Key;

                var cmdTab = CmdMgr.GetCommandTab((int)docType, cmdGroup.Name);

                if (cmdTab == null)
                {
                    cmdTab = CmdMgr.AddCommandTab((int)docType, cmdGroup.Name);
                }

                if (cmdTab != null)
                {
                    var cmdIds = cmdGrp.Select(c => c.CmdId).ToArray();
                    var txtTypes = cmdGrp.Select(c => (int)c.TextType).ToArray();

                    var cmdBox = TryFindCommandTabBox(cmdTab, cmdIds);

                    if (cmdBox == null)
                    {
                        cmdBox = cmdTab.AddCommandTabBox();
                    }
                    else
                    {
                        if (!IsCommandTabBoxChanged(cmdBox, cmdIds, txtTypes))
                        {
                            continue;
                        }
                        else
                        {
                            ClearCommandTabBox(cmdBox);
                        }
                    }

                    if (!cmdBox.AddCommands(cmdIds, txtTypes))
                    {
                        throw new InvalidOperationException("Failed to add commands to commands tab box");
                    }
                }
                else
                {
                    throw new NullReferenceException("Failed to create command tab box");
                }
            }
        }

        private CommandTabBox TryFindCommandTabBox(ICommandTab cmdTab, int[] cmdIds)
        {
            var cmdBoxesArr = cmdTab.CommandTabBoxes() as object[];

            if (cmdBoxesArr != null)
            {
                var cmdBoxes = cmdBoxesArr.Cast<CommandTabBox>().ToArray();

                var cmdBoxGroup = cmdBoxes.GroupBy(b =>
                {
                    object existingCmds;
                    object existingTextStyles;
                    b.GetCommands(out existingCmds, out existingTextStyles);

                    if (existingCmds is int[])
                    {
                        return ((int[])existingCmds).Intersect(cmdIds).Count();
                    }
                    else
                    {
                        return 0;
                    }
                }).OrderByDescending(g => g.Key).FirstOrDefault();

                if (cmdBoxGroup != null)
                {
                    if (cmdBoxGroup.Key > 0)
                    {
                        return cmdBoxGroup.FirstOrDefault();
                    }
                }

                return null;
            }

            return null;
        }

        private bool IsCommandTabBoxChanged(ICommandTabBox cmdBox, int[] cmdIds, int[] txtTypes)
        {
            object existingCmds;
            object existingTextStyles;
            cmdBox.GetCommands(out existingCmds, out existingTextStyles);

            if (existingCmds != null && existingTextStyles != null)
            {
                return !(existingCmds as int[]).SequenceEqual(cmdIds)
                    || !(existingTextStyles as int[]).SequenceEqual(txtTypes);
            }

            return true;
        }

        private void ClearCommandTabBox(ICommandTabBox cmdBox)
        {
            object existingCmds;
            object existingTextStyles;
            cmdBox.GetCommands(out existingCmds, out existingTextStyles);

            if (existingCmds != null)
            {
                cmdBox.RemoveCommands(existingCmds as int[]);
            }
        }

        private bool CompareIDs(IEnumerable<int> storedIDs, IEnumerable<int> addinIDs)
        {
            var storedList = storedIDs.ToList();
            var addinList = addinIDs.ToList();

            addinList.Sort();
            storedList.Sort();

            return addinList.SequenceEqual(storedIDs);
        }

        public void Dispose()
        {
            foreach (var grp in m_CommandGroups.Keys)
            {
                m_Logger.Log($"Removing group: {grp.Id}");
                CmdMgr.RemoveCommandGroup(grp.Id);
            }

            m_CommandGroups.Clear();

            if (Marshal.IsComObject(CmdMgr))
            {
                Marshal.ReleaseComObject(CmdMgr);
            }

            CmdMgr = null;
        }
    }
}
