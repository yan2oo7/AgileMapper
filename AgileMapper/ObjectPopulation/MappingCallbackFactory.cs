﻿namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System.Linq.Expressions;
    using Api.Configuration;
    using Configuration;
    using Members;

    internal class MappingCallbackFactory : UserConfiguredItemBase
    {
        private readonly ConfiguredLambdaInfo _callbackLambda;

        public MappingCallbackFactory(
            MappingConfigInfo configInfo,
            CallbackPosition callbackPosition,
            ConfiguredLambdaInfo callbackLambda,
            QualifiedMember targetMember)
            : base(configInfo, targetMember)
        {
            CallbackPosition = callbackPosition;
            _callbackLambda = callbackLambda;
        }

        protected CallbackPosition CallbackPosition { get; }

        public virtual bool AppliesTo(CallbackPosition callbackPosition, IMappingData data)
            => (CallbackPosition == callbackPosition) && base.AppliesTo(data);

        public Expression Create(IMemberMappingContext context)
        {
            var callback = _callbackLambda.GetBody(context);
            var condition = GetConditionOrNull(context);

            if (condition != null)
            {
                return Expression.IfThen(condition, callback);
            }

            return callback;
        }
    }
}