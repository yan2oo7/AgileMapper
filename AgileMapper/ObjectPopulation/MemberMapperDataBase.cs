namespace AgileObjects.AgileMapper.ObjectPopulation
{
    using System.Collections.Generic;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif
    using Members;

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
        }

        protected abstract IMapperDataValuesSource Values { get; }

        public ObjectMapperData Parent { get; }

        public ParameterExpression MappingDataObject => Values.MappingDataObject;

        public IList<Expression> RootObjects => Values.RootObjects;

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
    }
}