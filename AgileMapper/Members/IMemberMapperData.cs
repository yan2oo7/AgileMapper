﻿namespace AgileObjects.AgileMapper.Members
{
    using System.Collections.Generic;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using ObjectPopulation;

    internal interface IMemberMapperData : IQualifiedMemberContext
    {
        new ObjectMapperData Parent { get; }

        MapperDataContext Context { get; }

        Expression ParentObject { get; }

        ParameterExpression MappingDataObject { get; }

        IList<Expression> RootObjects { get; }

        Expression SourceObject { get; }

        Expression TargetObject { get; }

        Expression CreatedObject { get; }

        Expression ElementIndex { get; }

        Expression ElementKey { get; }

        Expression TargetInstance { get; }
    }
}