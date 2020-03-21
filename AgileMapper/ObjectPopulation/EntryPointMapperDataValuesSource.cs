namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using Extensions.Internal;
    using Members;
    using NetStandardPolyfills;

    internal class EntryPointMapperDataValuesSource : IMapperDataValuesSource
    {
        private readonly ObjectMapperData _mapperData;
        private readonly Type _mappingDataType;
        private Expression _parent;
        private Expression _targetInstance;
        private ParameterExpression _localVariable;
        private Expression _createdObject;
        private Expression _elementIndex;
        private Expression _elementKey;

        public EntryPointMapperDataValuesSource(ObjectMapperData entryPointMapperData)
        {
            _mapperData = entryPointMapperData;
            _mappingDataType = typeof(IMappingData<,>).MakeGenericType(SourceType, TargetType);
            MappingDataObject = CreateMappingDataObject();
            RootObjects = new Expression[] { MappingDataObject };
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
            return _mapperData.EnumerablePopulationBuilder?.TargetVariable ??
                   Expression.Variable(TargetType, TargetType.GetVariableNameInCamelCase());
        }

        public Expression CreatedObject
            => _createdObject ?? (_createdObject = GetMappingDataObjectProperty(nameof(CreatedObject)));

        public Expression ElementIndex
            => _elementIndex ?? (_elementIndex = GetElementIndexAccess());

        private Expression GetElementIndexAccess()
        {
            return _mapperData.DeclaredTypeMapperData?.ElementIndex ??
                   GetMappingDataProperty(nameof(ElementIndex));
        }

        public Expression ElementKey
            => _elementKey ?? (_elementKey = GetElementKeyAccess());

        private Expression GetElementKeyAccess()
        {
            return _mapperData.DeclaredTypeMapperData?.ElementKey ??
                   GetMappingDataProperty(nameof(ElementKey));
        }

        public IList<Expression> RootObjects { get; }
    }
}