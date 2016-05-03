﻿namespace AgileObjects.AgileMapper.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Shouldly;
    using TestClasses;
    using Xunit;

    public class WhenMappingDerivedTypes
    {
        [Fact]
        public void ShouldMapARootComplexTypeFromItsAssignedType()
        {
            object source = new Product { Price = 100.00 };
            var result = Mapper.Map(source).ToNew<Product>();

            result.Price.ShouldBe(100.00);
        }

        [Fact]
        public void ShouldMapARootComplexTypeEnumerableFromItsAssignedType()
        {
            object source = new[] { new Product { Price = 10.01 } };
            var result = Mapper.Map(source).ToNew<IEnumerable<Product>>();

            result.First().Price.ShouldBe(10.01);
        }

        [Fact]
        public void ShouldMapARootComplexTypeEnumerableElementFromItsAssignedType()
        {
            var source = new object[] { new Product { Price = 9.99 } };
            var result = Mapper.Map(source).ToNew<IEnumerable<Product>>();

            result.First().Price.ShouldBe(9.99);
        }

        [Fact]
        public void ShouldMapAComplexTypeMemberFromItsAssignedType()
        {
            var source = new PublicProperty<object>
            {
                Value = new { Name = "Frank", Address = (object)new Address { Line1 = "Here!" } }
            };

            var result = Mapper.Map(source).ToNew<PublicProperty<PersonViewModel>>();

            result.Value.ShouldNotBeNull();
            result.Value.Name.ShouldBe("Frank");
            result.Value.AddressLine1.ShouldBe("Here!");
        }

        [Fact]
        public void ShouldMapAComplexTypeMemberInACollectionFromItsAssignedType()
        {
            var sourceObjectId = Guid.NewGuid();

            var source = new object[]
            {
                new { Name = "Bob", Address = new { Line1 = "There!" } },
                new { Id = sourceObjectId.ToString(), Address = (object)new Address { Line1 = "Somewhere!" } }
            };

            var result = Mapper.Map(source).ToNew<ICollection<PersonViewModel>>();

            result.ShouldNotBeNull();
            result.Count.ShouldBe(2);

            result.First().Id.ShouldBeDefault();
            result.First().Name.ShouldBe("Bob");
            result.First().AddressLine1.ShouldBe("There!");

            result.Second().Id.ShouldBe(sourceObjectId);
            result.Second().Name.ShouldBeNull();
            result.Second().AddressLine1.ShouldBe("Somewhere!");
        }
    }
}
