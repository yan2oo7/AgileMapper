namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System;
    using System.Globalization;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using Extensions.Internal;
    using Members;
    using NetStandardPolyfills;

    internal interface IMapperDataObjectsSource
    {
        ParameterExpression MappingDataObject { get; }

        Expression Parent { get; }

        Expression Source { get; }

        Expression Target { get; }

        Expression TargetInstance { get; }

        Expression CreatedObject { get; }

        Expression EnumerableIndex { get; }
    }

    internal class EntryPointMapperDataObjectsSource : IMapperDataObjectsSource
    {
        private readonly ObjectMapperData _mapperData;
        private readonly Type _mappingDataType;
        private Expression _parent;
        private Expression _targetInstance;
        private ParameterExpression _localVariable;
        private Expression _createdObject;
        private Expression _enumerableIndex;

        public EntryPointMapperDataObjectsSource(ObjectMapperData entryPointMapperData)
        {
            _mapperData = entryPointMapperData;
            MappingDataObject = CreateMappingDataObject();
            _mappingDataType = typeof(IMappingData<,>).MakeGenericType(SourceType, TargetType);
            Source = GetMappingDataProperty(Member.RootSourceMemberName);
            Target = GetMappingDataObjectProperty(Member.RootTargetMemberName);
        }

        private ParameterExpression CreateMappingDataObject()
        {
            var mdType = typeof(IObjectMappingData<,>).MakeGenericType(SourceType, TargetType);

            var parent = _mapperData.Parent;
            var variableNameIndex = default(int?);

            while (parent != null)
            {
                if (parent.MappingDataObject.Type == mdType)
                {
                    variableNameIndex = variableNameIndex.HasValue ? (variableNameIndex + 1) : 2;
                }

                parent = parent.Parent;
            }

            var mappingDataVariableName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}To{1}Data{2}",
                SourceType.GetShortVariableName(),
                TargetType.GetShortVariableName().ToPascalCase(),
                variableNameIndex);

            return Expression.Parameter(mdType, mappingDataVariableName);
        }

        private Expression GetMappingDataProperty(string propertyName)
        {
            var property = _mappingDataType.GetPublicInstanceProperty(propertyName);

            return Expression.Property(MappingDataObject, property);
        }

        protected Expression GetMappingDataObjectProperty(string propertyName)
            => Expression.Property(MappingDataObject, propertyName);

        private Type SourceType => _mapperData.SourceType;

        private Type TargetType => _mapperData.TargetType;

        public ParameterExpression MappingDataObject { get; }

        public Expression Parent => _parent ?? (_parent = GetParent());

        private Expression GetParent()
        {
            return _mapperData.DeclaredTypeMapperData?.ParentObject ??
                    GetMappingDataObjectProperty(nameof(Parent));
        }

        public Expression Source { get; }

        public Expression Target { get; }

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
            return _mapperData.EnumerablePopulationBuilder?.TargetVariable ?? 
                    Expression.Variable(TargetType, TargetType.GetVariableNameInCamelCase());
        }

        public Expression CreatedObject
            => _createdObject ?? (_createdObject = GetMappingDataObjectProperty(nameof(CreatedObject)));

        public Expression EnumerableIndex
            => _enumerableIndex ?? (_enumerableIndex = GetEnumerableIndex());

        private Expression GetEnumerableIndex()
        {
            return _mapperData.DeclaredTypeMapperData?.EnumerableIndex ??
                    GetMappingDataProperty(nameof(EnumerableIndex));
        }
    }

    internal abstract class MemberMapperDataBase : BasicMapperData
    {
        protected MemberMapperDataBase(
            MappingRuleSet ruleSet,
            IQualifiedMember sourceMember,
            QualifiedMember targetMember,
            MapperContext mapperContext,
            ObjectMapperData parent)
            : base(
                ruleSet,
                sourceMember.Type,
                targetMember.Type,
                sourceMember,
                targetMember,
                parent)
        {
            MapperContext = mapperContext;
            Parent = parent;
            MappingDataObject = CreateMappingDataObject();
            MappingDataType = typeof(IMappingData<,>).MakeGenericType(SourceType, TargetType);
            SourceObject = GetMappingDataProperty(MappingDataType, Member.RootSourceMemberName);
            TargetObject = GetMappingDataProperty(Member.RootTargetMemberName);
        }

        public MapperContext MapperContext { get; }

        public ObjectMapperData Parent { get; }

        public ParameterExpression MappingDataObject { get; }

        public Expression SourceObject { get; set; }

        public Expression TargetObject { get; set; }

        protected ParameterExpression CreateMappingDataObject()
        {
            var mdType = typeof(IObjectMappingData<,>).MakeGenericType(SourceType, TargetType);

            var parent = Parent;
            var variableNameIndex = default(int?);

            while (parent != null)
            {
                if (parent.MappingDataObject.Type == mdType)
                {
                    variableNameIndex = variableNameIndex.HasValue ? (variableNameIndex + 1) : 2;
                }

                parent = parent.Parent;
            }

            var mappingDataVariableName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}To{1}Data{2}",
                SourceType.GetShortVariableName(),
                TargetType.GetShortVariableName().ToPascalCase(),
                variableNameIndex);

            return Expression.Parameter(mdType, mappingDataVariableName);
        }

        protected Type MappingDataType { get; }

        protected Expression GetEnumerableIndexAccess()
            => GetMappingDataProperty(MappingDataType, "EnumerableIndex");

        protected Expression GetParentObjectAccess()
            => GetMappingDataProperty(nameof(Parent));

        protected Expression GetMappingDataProperty(Type mappingDataType, string propertyName)
        {
            var property = mappingDataType.GetPublicInstanceProperty(propertyName);

            return Expression.Property(MappingDataObject, property);
        }

        protected Expression GetMappingDataProperty(string propertyName)
            => Expression.Property(MappingDataObject, propertyName);
    }
}