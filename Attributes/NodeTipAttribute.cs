using System;

namespace Amlos.AI
{
    /// <summary>
    /// An attribute that allow ai editor to display tooltip for the type of node
    /// </summary>
    [System.AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NodeTipAttribute : Attribute
    {
        readonly string tip;

        /// <summary>
        /// the tip
        /// </summary>
        /// <param name="tip"></param>
        public NodeTipAttribute(string tip)
        {
            this.tip = tip;
        }

        public string Tip
        {
            get { return tip; }
        }
    }

    /// <summary>
    /// An attribute that allow ai editor to display alternative name for the type of node
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AliasAttribute : Attribute
    {
        readonly string alias;

        /// <summary>
        /// the tip
        /// </summary>
        /// <param name="tip"></param>
        public AliasAttribute(string tip)
        {
            this.alias = tip;
        }

        public string Tip
        {
            get { return alias; }
        }
    }
}