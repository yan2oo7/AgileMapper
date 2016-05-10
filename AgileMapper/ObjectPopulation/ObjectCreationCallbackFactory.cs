﻿namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System;
    using System.Linq.Expressions;
    using Api.Configuration;
    using DataSources;
    using Extensions;
    using Members;

    internal class ObjectCreationCallbackFactory : UserConfiguredItemBase
    {
        private readonly Type _creationTargetType;
        private readonly CallbackPosition _callbackPosition;
        private readonly LambdaExpression _callbackLambda;
        private readonly Func<IMemberMappingContext, Expression[]> _parameterReplacementsFactory;

        public ObjectCreationCallbackFactory(
            MappingConfigInfo configInfo,
            Type mappingTargetType,
            Type creationTargetType,
            CallbackPosition callbackPosition,
            LambdaExpression callbackLambda,
            Func<IMemberMappingContext, Expression[]> parameterReplacementsFactory)
            : base(configInfo, mappingTargetType, QualifiedMember.All)
        {
            _creationTargetType = creationTargetType;
            _callbackPosition = callbackPosition;
            _callbackLambda = callbackLambda;
            _parameterReplacementsFactory = parameterReplacementsFactory;
        }

        public override bool AppliesTo(IMemberMappingContext context)
            => _creationTargetType.IsAssignableFrom(context.TargetVariable.Type) && base.AppliesTo(context);

        public ObjectCreationCallback GetCallback(IMemberMappingContext context)
        {
            var parameterReplacements = _parameterReplacementsFactory.Invoke(context);
            var callback = _callbackLambda.ReplaceParameters(parameterReplacements);
            var condition = GetCondition(context);

            return new ObjectCreationCallback(_callbackPosition, callback, condition);
        }
    }
}