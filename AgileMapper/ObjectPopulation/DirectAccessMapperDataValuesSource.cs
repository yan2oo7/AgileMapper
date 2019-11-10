namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System.Collections.Generic;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using Enumerables;
    using Extensions.Internal;
    using Members;

    internal class DirectAccessMapperDataValuesSource : IMapperDataValuesSource
    {
        private readonly IMemberMapperData _mapperData;
        private readonly IMemberMapperData _originalMapperData;
        private readonly EnumerablePopulationBuilder _enumerablePopulationBuilder;
        private Expression _targetInstance;
        private ParameterExpression _localVariable;
        private Expression _createdObject;
        private Expression _enumerableIndex;

        public DirectAccessMapperDataValuesSource(
            IMemberMapperData mapperData,
            IMemberMapperData originalMapperData,
            EnumerablePopulationBuilder enumerablePopulationBuilder)
        {
            _mapperData = mapperData;
            _originalMapperData = originalMapperData;
            _enumerablePopulationBuilder = enumerablePopulationBuilder;
            Source = mapperData.SourceMember.GetQualifiedAccess(mapperData.Parent.SourceObject);
            Target = mapperData.TargetMember.GetQualifiedAccess(mapperData.Parent.TargetInstance);
            RootObjects = new[] { Source, Target };
        }

        public ParameterExpression MappingDataObject => null;

        public Expression Parent => _mapperData.MappingDataObject;

        public Expression Source { get; set; }

        public Expression Target { get; set; }

        public Expression TargetInstance
        {
            get => _targetInstance ?? (_targetInstance = GetTargetInstance());
            set => _targetInstance = value;
        }

        private Expression GetTargetInstance()
            => _mapperData.Context.UseLocalVariable ? LocalVariable : Target;

        public ParameterExpression LocalVariable
        {
            get => _localVariable ?? (_localVariable = CreateLocalVariable());
            set => _localVariable = value;
        }

        private ParameterExpression CreateLocalVariable()
        {
            return _enumerablePopulationBuilder?.TargetVariable ??
                   Expression.Variable(Target.Type, Target.Type.GetVariableNameInCamelCase());
        }

        public Expression CreatedObject
            => _createdObject ?? (_createdObject = TargetInstance);

        public Expression EnumerableIndex
            => _enumerableIndex ?? (_enumerableIndex = GetEnumerableIndex());

        public IList<Expression> RootObjects { get; }

        private Expression GetEnumerableIndex()
        {
            return _originalMapperData?.EnumerableIndex ??
                   _enumerablePopulationBuilder?.Counter ??
                   (Expression)default(int?).ToConstantExpression();
        }
    }
}