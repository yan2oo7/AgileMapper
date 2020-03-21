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

    internal abstract class MemberMapperDataBase : QualifiedMemberContext
    {
        protected MemberMapperDataBase(
            MappingRuleSet ruleSet,
            IQualifiedMember sourceMember,
            QualifiedMember targetMember,
            ObjectMapperData parent,
            MapperContext mapperContext)
            : base(
                ruleSet,
                sourceMember.Type,
                targetMember.Type,
                sourceMember,
                targetMember,
                parent,
                mapperContext)
        {
            Parent = parent;
            MappingDataType = typeof(IMappingData<,>).MakeGenericType(SourceType, TargetType);
            SourceObject = GetMappingDataProperty(MappingDataType, Member.RootSourceMemberName);
            TargetObject = GetMappingDataProperty(Member.RootTargetMemberName);
        }

        protected abstract IMapperDataValuesSource Values { get; }

        public ObjectMapperData Parent { get; }

        public ParameterExpression MappingDataObject => Values.MappingDataObject;

        public Expression ParentObject => Values.Parent;

        public Expression SourceObject
        {
            get => Values.Source;
            set => Values.Source = value;
        }

        public Expression TargetObject
        {
            get => Values.Target;
            set => Values.Target = value;
        }

        public Expression CreatedObject => Values.CreatedObject;

        public Expression ElementIndex => Values.ElementIndex;

        public Expression ElementKey => Values.ElementKey;

        public Expression TargetInstance
        {
            get => Values.TargetInstance;
            set => Values.TargetInstance = value;
        }

        public ParameterExpression LocalVariable
        {
            get => Values.LocalVariable;
            set => Values.LocalVariable = value;
        }

        protected Type MappingDataType { get; }

        protected Expression GetElementIndexAccess()
            => GetMappingDataProperty(MappingDataType, "ElementIndex");

        protected Expression GetElementKeyAccess()
            => GetMappingDataProperty(MappingDataType, "ElementKey");

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