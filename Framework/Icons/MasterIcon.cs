﻿//**********************
//SwEx.AddIn - development tools for SOLIDWORKS add-ins
//Copyright(C) 2019 www.codestack.net
//License: https://github.com/codestackdev/swex-addin/blob/master/LICENSE
//Product URL: https://www.codestack.net/labs/solidworks/swex/add-in/
//**********************

using CodeStack.SwEx.Common.Icons;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace CodeStack.SwEx.AddIn.Icons
{
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public class MasterIcon : CommandGroupIcon
    {
        private readonly Image m_Icon;

        internal protected MasterIcon(Image icon)
        {
            m_Icon = icon;
        }

        public override IEnumerable<IconSizeInfo> GetHighResolutionIconSizes()
        {
            yield return new IconSizeInfo(m_Icon, new Size(20, 20));
            yield return new IconSizeInfo(m_Icon, new Size(32, 32));
            yield return new IconSizeInfo(m_Icon, new Size(40, 40));
            yield return new IconSizeInfo(m_Icon, new Size(64, 64));
            yield return new IconSizeInfo(m_Icon, new Size(96, 96));
            yield return new IconSizeInfo(m_Icon, new Size(128, 128));
        }

        public override IEnumerable<IconSizeInfo> GetIconSizes()
        {
            yield return new IconSizeInfo(m_Icon, new Size(16, 16));
            yield return new IconSizeInfo(m_Icon, new Size(24, 24));
        }
    }
}
