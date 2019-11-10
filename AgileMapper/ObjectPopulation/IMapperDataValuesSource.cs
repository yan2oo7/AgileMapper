namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System.Collections.Generic;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif

    internal interface IMapperDataValuesSource
    {
        ParameterExpression MappingDataObject { get; }

        Expression Parent { get; }

        Expression Source { get; set; }

        Expression Target { get; set; }

        Expression TargetInstance { get; set; }

        ParameterExpression LocalVariable { get; set; }

        Expression CreatedObject { get; }

        Expression EnumerableIndex { get; }

        IList<Expression> RootObjects { get; }
    }
}