﻿namespace AgileObjects.AgileMapper.Plans
{
    using System;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using ObjectPopulation.RepeatedMappings;
    using ReadableExpressions;
    using ReadableExpressions.Extensions;

    internal class RepeatedMappingMappingPlanFunction : IMappingPlanFunction
    {
        private readonly IRepeatedMapperFunc _mapperFunc;
        private CommentExpression _summary;

        public RepeatedMappingMappingPlanFunction(IRepeatedMapperFunc mapperFunc)
        {
            _mapperFunc = mapperFunc;
        }

        public Type SourceType => _mapperFunc.SourceType;

        public Type TargetType => _mapperFunc.TargetType;

        public CommentExpression Summary
            => _summary ??= ReadableExpression.Comment(GetMappingDescription());

        private string GetMappingDescription(string linePrefix = null)
        {
            return $@"
{linePrefix}Map {SourceType.GetFriendlyName()} -> {TargetType.GetFriendlyName()}
{linePrefix}Repeated Mapping Mapper

";
        }

        public LambdaExpression Mapping => _mapperFunc.Mapping;

        public string ToSourceCode()
        {
            var description = GetMappingDescription(linePrefix: "// ");

            return description + Mapping.ToReadableString();
        }
    }
}