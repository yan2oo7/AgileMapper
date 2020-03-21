namespace AgileObjects.AgileMapper.ObjectPopulation
{
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using Extensions.Internal;
    using Members;
    using Members.Sources;

    internal class SimpleMemberMapperData : MemberMapperDataBase, IMemberMapperData
    {
        private SimpleMemberMapperData(
            IQualifiedMember sourceMember,
            IMemberMapperData memberMapperData)
            : base(
                memberMapperData.RuleSet,
                sourceMember,
                memberMapperData.TargetMember,
                memberMapperData.Parent,
                memberMapperData.MapperContext)
        {
            ElementIndexValue = Parent.ElementIndex;
            Values = new DirectAccessMapperDataValuesSource(this, memberMapperData, null);
        }

        private SimpleMemberMapperData(
            IQualifiedMember sourceMember,
            QualifiedMember targetMember,
            ObjectMapperData enumerableMapperData)
            : base(
                enumerableMapperData.RuleSet,
                sourceMember,
                targetMember,
                enumerableMapperData,
                enumerableMapperData.MapperContext)
        {
            ElementIndexValue = enumerableMapperData.EnumerablePopulationBuilder.Counter.GetConversionTo<int?>();

            Values = new DirectAccessMapperDataValuesSource(
                this,
                enumerableMapperData,
                enumerableMapperData.EnumerablePopulationBuilder);
        }

        #region Factory Method

        public static SimpleMemberMapperData Create(Expression sourceValue, IMemberMapperData mapperData)
        {
            if (!mapperData.TargetMember.IsEnumerable)
            {
                return new SimpleMemberMapperData(sourceValue.ToSourceMember(mapperData.MapperContext), mapperData);
            }

            var enumerableMapperData = (ObjectMapperData)mapperData;
            var membersSource = new ElementMembersSource(enumerableMapperData);

            return new SimpleMemberMapperData(
                membersSource.GetSourceMember().WithType(sourceValue.Type),
                membersSource.GetTargetMember(),
                enumerableMapperData);
        }

        #endregion

        protected override IMapperDataValuesSource Values { get; }

        public override bool IsEntryPoint => false;

        public MapperDataContext Context => null;

        public Expression RootMappingDataObject => Parent.RootMappingDataObject;

        public Expression ElementIndexValue { get; }
    }
}