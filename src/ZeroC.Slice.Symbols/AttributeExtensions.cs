// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Extension methods for querying Slice attribute lists.</summary>
internal static class AttributeExtensions
{
    extension(IList<Attribute> attributes)
    {
        /// <summary>Checks if the attribute list contains a specific directive.</summary>
        internal bool HasAttribute(string directive) =>
            attributes.Any(a => a.Directive == directive);

        /// <summary>Finds an attribute by directive.</summary>
        internal Attribute? FindAttribute(string directive)
        {
            foreach (Attribute attr in attributes)
            {
                if (attr.Directive == directive)
                {
                    return attr;
                }
            }
            return null;
        }

        /// <summary>Returns all cs::attribute attributes from the list.</summary>
        internal IEnumerable<Attribute> CsAttributes() =>
            attributes.Where(a => a.Directive == Attribute.CsAttribute);
    }
}
