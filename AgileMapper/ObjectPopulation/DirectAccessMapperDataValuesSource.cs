namespace AgileObjects.AgileMapper.ObjectPopulation
{
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
        private readonly IMemberMapperData _declaredTypeMapperData;
        private readonly EnumerablePopulationBuilder _enumerablePopulationBuilder;
        private Expression _targetInstance;
        private ParameterExpression _localVariable;
        private Expression _createdObject;
        private Expression _enumerableIndex;

        public DirectAccessMapperDataValuesSource(
            IMemberMapperData mapperData,
            IMemberMapperData declaredTypeMapperData,
            EnumerablePopulationBuilder enumerablePopulationBuilder)
        {
            _mapperData = mapperData;
            _declaredTypeMapperData = declaredTypeMapperData;
            _enumerablePopulationBuilder = enumerablePopulationBuilder;
            Source = mapperData.SourceMember.GetQualifiedAccess(mapperData.Parent.SourceObject);
            Target = mapperData.TargetMember.GetQualifiedAccess(mapperData.Parent.TargetObject);
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

        private Expression GetEnumerableIndex()
        {
            return _declaredTypeMapperData?.EnumerableIndex ??
                   _enumerablePopulationBuilder?.Counter ??
                   (Expression)default(int?).ToConstantExpression();
        }
    }
}