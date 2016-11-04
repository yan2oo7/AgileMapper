﻿namespace AgileObjects.AgileMapper.DataSources
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Extensions;
    using Members;
    using ObjectPopulation;

    internal static class InlineMappingFactory
    {
        public static Expression GetDerivedTypeMapping(
            IObjectMappingData declaredTypeMappingData,
            Expression sourceValue,
            Type targetType)
        {
            var declaredTypeMapperData = declaredTypeMappingData.MapperData;

            var targetValue = declaredTypeMapperData.TargetMember.IsReadable
                ? declaredTypeMapperData.TargetObject.GetConversionTo(targetType)
                : Expression.Default(targetType);

            var derivedTypeMappingData = declaredTypeMappingData.WithTypes(sourceValue.Type, targetType);

            if (declaredTypeMappingData.IsRoot)
            {
                return GetDerivedTypeRootMapping(derivedTypeMappingData, sourceValue, targetValue);
            }

            if (declaredTypeMapperData.TargetMemberIsEnumerableElement())
            {
                return GetDerivedTypeElementMapping(derivedTypeMappingData, sourceValue, targetValue);
            }

            return GetDerivedTypeChildMapping(derivedTypeMappingData, sourceValue, targetValue);
        }

        private static Expression GetDerivedTypeRootMapping(
            IObjectMappingData derivedTypeMappingData,
            Expression sourceValue,
            Expression targetValue)
        {
            var declaredTypeMapperData = derivedTypeMappingData.DeclaredTypeMappingData.MapperData;
            var derivedTypeMapperData = derivedTypeMappingData.MapperData;
            var derivedTypeMapper = derivedTypeMappingData.Mapper;

            var inlineMappingBlock = GetInlineMappingBlock(
                derivedTypeMapper,
                derivedTypeMapperData,
                MappingDataFactory.ForRootMethod,
                sourceValue,
                targetValue,
                Expression.Property(declaredTypeMapperData.MappingDataObject, "MappingContext"));

            return inlineMappingBlock;
        }

        private static Expression GetDerivedTypeElementMapping(
            IObjectMappingData derivedTypeMappingData,
            Expression sourceElementValue,
            Expression targetElementValue)
        {
            var declaredTypeMapperData = derivedTypeMappingData.DeclaredTypeMappingData.MapperData;

            return GetElementMapping(
                sourceElementValue,
                targetElementValue,
                Expression.Property(declaredTypeMapperData.EnumerableIndex, "Value"),
                derivedTypeMappingData,
                declaredTypeMapperData);
        }

        private static Expression GetDerivedTypeChildMapping(
            IObjectMappingData derivedTypeMappingData,
            Expression sourceValue,
            Expression targetValue)
        {
            var declaredTypeMapperData = derivedTypeMappingData.DeclaredTypeMappingData.MapperData;
            var derivedTypeMapperData = derivedTypeMappingData.MapperData;

            return GetChildMapping(
                derivedTypeMapperData.SourceMember,
                sourceValue,
                targetValue,
                declaredTypeMapperData.EnumerableIndex,
                declaredTypeMapperData.DataSourceIndex,
                derivedTypeMappingData,
                derivedTypeMapperData,
                declaredTypeMapperData);
        }

        public static Expression GetChildMapping(int dataSourceIndex, IMemberMappingData childMappingData)
        {
            var childMapperData = childMappingData.MapperData;
            var relativeMember = childMapperData.SourceMember.RelativeTo(childMapperData.SourceMember);
            var sourceMemberAccess = relativeMember.GetQualifiedAccess(childMapperData.SourceObject);

            return GetChildMapping(
                relativeMember,
                sourceMemberAccess,
                dataSourceIndex,
                childMappingData);
        }

        public static Expression GetChildMapping(
            IQualifiedMember sourceMember,
            Expression sourceMemberAccess,
            int dataSourceIndex,
            IMemberMappingData childMappingData)
        {
            var childMapperData = childMappingData.MapperData;
            var targetMemberAccess = childMapperData.GetTargetMemberAccess();

            return GetChildMapping(
                sourceMember,
                sourceMemberAccess,
                targetMemberAccess,
                childMapperData.Parent.EnumerableIndex,
                dataSourceIndex,
                childMappingData.Parent,
                childMapperData,
                childMapperData.Parent);
        }

        private static Expression GetChildMapping(
            IQualifiedMember sourceMember,
            Expression sourceValue,
            Expression targetValue,
            Expression enumerableIndex,
            int dataSourceIndex,
            IObjectMappingData parentMappingData,
            IMemberMapperData childMapperData,
            ObjectMapperData declaredTypeMapperData)
        {
            var childMappingData = ObjectMappingDataFactory.ForChild(
                sourceMember,
                childMapperData.TargetMember,
                dataSourceIndex,
                parentMappingData);

            if (childMappingData.MapperKey.MappingTypes.RuntimeTypesNeeded)
            {
                return declaredTypeMapperData.GetMapCall(sourceValue, childMapperData.TargetMember, dataSourceIndex);
            }

            if (TargetMemberIsRecursive(childMapperData))
            {
                var mapRecursionCall = GetMapRecursionCallFor(
                    childMappingData,
                    sourceValue,
                    dataSourceIndex,
                    declaredTypeMapperData);

                return mapRecursionCall;
            }

            var childMapper = childMappingData.Mapper;

            var inlineMappingBlock = GetInlineMappingBlock(
                childMapper,
                childMappingData.MapperData,
                MappingDataFactory.ForChildMethod,
                sourceValue,
                targetValue,
                enumerableIndex,
                Expression.Constant(childMapperData.TargetMember.RegistrationName),
                Expression.Constant(dataSourceIndex),
                declaredTypeMapperData.MappingDataObject);

            return inlineMappingBlock;
        }

        private static bool TargetMemberIsRecursive(IMemberMapperData mapperData)
        {
            if (mapperData.TargetMember.IsRecursive)
            {
                return true;
            }

            var parentMapperData = mapperData.Parent;

            while (!parentMapperData.IsForStandaloneMapping)
            {
                if (parentMapperData.TargetMember.IsRecursive)
                {
                    // The target member we're mapping right now isn't recursive,
                    // but it's being mapped as part of the mapping of a recursive
                    // member. We therefore check if this member recurses later;
                    // if so we'll map it by calling MapRecursion:
                    return TargetMemberRecursesWithin(
                        parentMapperData.TargetMember,
                        mapperData.TargetMember.LeafMember);
                }

                parentMapperData = parentMapperData.Parent;
            }

            return false;
        }

        private static bool TargetMemberRecursesWithin(QualifiedMember parentMember, Member member)
        {
            var nonSimpleChildMembers = GlobalContext.Instance
                .MemberFinder
                .GetWriteableMembers(parentMember.Type)
                .Where(m => !m.IsSimple)
                .ToArray();

            if (nonSimpleChildMembers.Contains(member))
            {
                var childMember = parentMember.Append(member);

                return childMember.IsRecursive;
            }

            return nonSimpleChildMembers.Any(m => TargetMemberRecursesWithin(parentMember.Append(m), member));
        }

        private static Expression GetMapRecursionCallFor(
            IObjectMappingData childMappingData,
            Expression sourceValue,
            int dataSourceIndex,
            ObjectMapperData declaredTypeMapperData)
        {
            var childMapperData = childMappingData.MapperData;

            childMapperData.RegisterRequiredMapperFunc(childMappingData);

            var mapRecursionCall = declaredTypeMapperData.GetMapRecursionCall(
                sourceValue,
                childMapperData.TargetMember,
                dataSourceIndex);

            return mapRecursionCall;
        }

        public static Expression GetElementMapping(
            Expression sourceElementValue,
            Expression targetElementValue,
            IObjectMappingData enumerableMappingData)
        {
            var declaredTypeEnumerableMapperData = enumerableMappingData.MapperData;

            var elementMapperData = new ElementMapperData(
                sourceElementValue,
                targetElementValue,
                declaredTypeEnumerableMapperData);

            var elementMappingData = ObjectMappingDataFactory.ForElement(
                elementMapperData.SourceMember,
                elementMapperData.TargetMember,
                enumerableMappingData);

            if (elementMappingData.MapperKey.MappingTypes.RuntimeTypesNeeded)
            {
                return declaredTypeEnumerableMapperData.GetMapCall(sourceElementValue, targetElementValue);
            }

            return GetElementMapping(
                sourceElementValue,
                targetElementValue,
                declaredTypeEnumerableMapperData.EnumerablePopulationBuilder.Counter,
                elementMappingData,
                declaredTypeEnumerableMapperData);
        }

        private static Expression GetElementMapping(
            Expression sourceElementValue,
            Expression targetElementValue,
            Expression enumerableIndex,
            IObjectMappingData elementMappingData,
            IMemberMapperData declaredTypeMapperData)
        {
            var elementMapper = elementMappingData.Mapper;

            var inlineMappingBlock = GetInlineMappingBlock(
                elementMapper,
                elementMappingData.MapperData,
                MappingDataFactory.ForElementMethod,
                sourceElementValue,
                targetElementValue,
                enumerableIndex,
                declaredTypeMapperData.MappingDataObject);

            return inlineMappingBlock;
        }

        private static Expression GetInlineMappingBlock(
            IObjectMapper childMapper,
            IMemberMapperData childMapperData,
            MethodInfo createMethod,
            params Expression[] createMethodCallArguments)
        {
            if (childMapper.MappingExpression.NodeType != ExpressionType.Try)
            {
                return childMapper.MappingExpression;
            }

            var inlineMappingDataVariable = childMapperData.MappingDataObject;

            var createInlineMappingDataCall = GetCreateMappingDataCall(
                createMethod,
                childMapperData,
                createMethodCallArguments);

            var inlineMappingDataAssignment = Expression
                .Assign(inlineMappingDataVariable, createInlineMappingDataCall);

            var mappingTryCatch = (TryExpression)childMapper.MappingExpression;

            var updatedTryCatch = mappingTryCatch.Update(
                Expression.Block(inlineMappingDataAssignment, mappingTryCatch.Body),
                mappingTryCatch.Handlers,
                mappingTryCatch.Finally,
                mappingTryCatch.Fault);

            var mappingBlock = Expression.Block(new[] { inlineMappingDataVariable }, updatedTryCatch);

            return mappingBlock;
        }

        private static Expression GetCreateMappingDataCall(
            MethodInfo createMethod,
            IBasicMapperData childMapperData,
            Expression[] createMethodCallArguments)
        {
            var inlineMappingTypes = new[] { childMapperData.SourceType, childMapperData.TargetType };

            return Expression.Call(
                createMethod.MakeGenericMethod(inlineMappingTypes),
                createMethodCallArguments);
        }
    }
}