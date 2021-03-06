﻿namespace AgileObjects.AgileMapper.Configuration
{
    using System.Collections.Generic;
    using Extensions;
    using Extensions.Internal;
    using Members;

    internal static class ConfiguredItemExtensions
    {
        public static TItem FindMatch<TItem>(this IList<TItem> items, IQualifiedMemberContext context)
            where TItem : UserConfiguredItemBase
        {
            return items?.FirstOrDefault(context, (ctx, item) => item.AppliesTo(ctx));
        }

        public static IList<TItem> FindRelevantMatches<TItem>(this IList<TItem> items, IQualifiedMemberContext context)
            where TItem : UserConfiguredItemBase
        {
            return items?.FilterToArray(context, (ctx, item) => item.CouldApplyTo(ctx)) ?? Enumerable<TItem>.EmptyArray;
        }

        public static IEnumerable<TItem> FindMatches<TItem>(this IEnumerable<TItem> items, IQualifiedMemberContext context)
            where TItem : UserConfiguredItemBase
        {
            return items?.Filter(context, (ctx, item) => item.AppliesTo(ctx)) ?? Enumerable<TItem>.Empty;
        }
    }
}