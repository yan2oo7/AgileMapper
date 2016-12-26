namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using Members;
    using Members.Population;

    internal static class MemberPopulationFactory
    {
        public static IEnumerable<IMemberPopulation> Create(IObjectMappingData mappingData)
        {
            var targetMembers = GlobalContext
                .Instance
                .MemberFinder
                .GetTargetMembers(mappingData.MapperData.TargetType)
                .Select(targetMember => mappingData.MapperData.TargetMember.Append(targetMember));

            return Create(targetMembers, mappingData);
        }

        public static IEnumerable<IMemberPopulation> Create(
            IEnumerable<QualifiedMember> targetMembers,
            IObjectMappingData mappingData)
        {
            return targetMembers.Select(tm => Create(tm, mappingData));
        }

        private static IMemberPopulation Create(QualifiedMember targetMember, IObjectMappingData mappingData)
        {
            var childMapperData = new ChildMemberMapperData(targetMember, mappingData.MapperData);

            Expression populateCondition;

            if (TargetMemberIsUnconditionallyIgnored(childMapperData, out populateCondition))
            {
                return MemberPopulation.IgnoredMember(childMapperData);
            }

            var childMappingData = mappingData.GetChildMappingData(childMapperData);

            var dataSources = childMapperData
                .MapperContext
                .DataSources
                .FindFor(childMappingData);

            if (dataSources.None)
            {
                return MemberPopulation.NoDataSource(childMapperData);
            }

            return new MemberPopulation(childMappingData, dataSources, populateCondition);
        }

        private static bool TargetMemberIsUnconditionallyIgnored(
            IMemberMapperData mapperData,
            out Expression populateCondition)
        {
            var configuredIgnore = mapperData
                .MapperContext
                .UserConfigurations
                .GetMemberIgnoreOrNull(mapperData);

            if (configuredIgnore == null)
            {
                populateCondition = null;
                return false;
            }

            populateCondition = configuredIgnore.GetConditionOrNull(mapperData);
            return (populateCondition == null);
        }
    }
}