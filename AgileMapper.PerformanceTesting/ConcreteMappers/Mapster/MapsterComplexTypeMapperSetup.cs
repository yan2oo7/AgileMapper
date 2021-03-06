﻿namespace AgileObjects.AgileMapper.PerformanceTesting.ConcreteMappers.Mapster
{
    using System.Collections.Generic;
    using System.Linq;
    using AbstractMappers;
    using global::Mapster;
    using static TestClasses.Complex;

    public class MapsterComplexTypeMapperSetup : ComplexTypeMapperSetupBase
    {
        public override void Initialise()
        {
        }

        protected override Foo SetupComplexTypeMapper(Foo foo)
        {
            TypeAdapterConfig<Foo, Foo>.NewConfig()
                .Map(dest => dest.Foos, src => src.Foos ?? new List<Foo>())
                .Map(dest => dest.FooArray, src => src.FooArray ?? new Foo[0])
                .Map(dest => dest.Ints, src => src.Ints ?? Enumerable.Empty<int>())
                .Map(dest => dest.IntArray, src => src.IntArray ?? new int[0])
                .Compile();

            return foo.Adapt<Foo, Foo>();
        }

        protected override void Reset()
            => TypeAdapterConfig<Foo, Foo>.Clear();
    }
}