using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using MainCore.Constraints;
using MainCore.Entities;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MainCore.Test
{
    [Trait("Mode", "Debug")]
    public class ArchitectureTest
    {
        private static readonly Architecture Architecture = new ArchLoader()
            .LoadAssemblies(
            System.Reflection.Assembly.Load("MainCore")
        ).Build();

        private readonly IObjectProvider<MethodMember> Handler =
            MethodMembers().That().HaveNameContaining("HandleAsync").And().AreStatic().As("Handler");

        private readonly IObjectProvider<Class> Request =
            Classes().That().AreAssignableTo(typeof(IConstraint)).As("Request");

        [Theory]
        [InlineData(typeof(ICommand), "Command", true)]
        [InlineData(typeof(ITask), "Task", false)]
        public void RequestShouldHaveCorrectName(System.Type interfaceType, string nameSuffix, bool shouldBeRecord)
        {
            var rule = Classes().That()
                .AreAssignableTo(interfaceType)
                .And()
                .AreNotAbstract();

            var shouldRule = rule
                .Should().BeSealed()
                .AndShould().HaveNameEndingWith(nameSuffix);

            if (shouldBeRecord)
            {
                shouldRule = shouldRule.AndShould().BeRecord();
            }

            shouldRule.Check(Architecture);
        }

        [Fact]
        public void HandlerAccessDatabaseShouldNotContainOtherHandler()
        {
            var otherhandler = Classes().That()
                .AreAssignableTo(typeof(Immediate.Handlers.Shared.IHandler<,>))
                .As("other handler");

            // 2026-10-20: tasks and feature commands are orchestrators - they read settings/jobs
            // from the database AND call other command handlers (that is what a flow is for;
            // ~20 tasks such as RaidListTask/DodgeTroopTask/ApplyBuildTemplateTask and feature
            // commands such as UseHeroResourceCommand/GetBuildPlanCommand/HandleResourceCommand/
            // GetCrannyProtectionCommand/TrainCheapestTroopCommand do exactly this). The rule is
            // kept for the small building-block commands (Update/Navigate/Misc/UI), where mixing
            // database access with calling other handlers is the real smell.
            var orchestrators = Classes().That()
                .ResideInNamespace(@"^MainCore\.(Tasks|Commands\.Features)(\..*)?$", true)
                .As("tasks and feature commands");

            var rule = MethodMembers().That()
                .Are(Handler)
                .And()
                .AreNotDeclaredIn(orchestrators)
                .And()
                .DependOnAny(typeof(AppDbContext))
                .Should()
                .NotDependOnAny(otherhandler);
            rule.Check(Architecture);
        }

        [Fact]
        public void HandlerShouldNotDependOnDialogService()
        {
            var rule = MethodMembers().That()
                .Are(Handler)
                .Should()
                .NotDependOnAny(typeof(IDialogService));
            rule.Check(Architecture);
        }

        [Fact]
        public void RequestShouldHaveCorrectConstraint()
        {
            var accountVillageRule = Classes().That()
                .Are(Request)
                .And()
                .DependOnAny(typeof(AccountId))
                .And()
                .DependOnAny(typeof(VillageId))
                .Should()
                .BeAssignableTo(typeof(IAccountVillageConstraint));

            accountVillageRule.Check(Architecture);

            var accountOnlyRule = Classes().That()
                .Are(Request)
                .And()
                .DependOnAny(typeof(AccountId))
                .And()
                .DoNotDependOnAny(typeof(VillageId))
                .Should()
                .BeAssignableTo(typeof(IAccountConstraint));
            accountOnlyRule.Check(Architecture);

            var villageOnlyRule = Classes().That()
                .Are(Request)
                .And()
                .DependOnAny(typeof(VillageId))
                .And()
                .DoNotDependOnAny(typeof(AccountId))
                .Should()
                .BeAssignableTo(typeof(IVillageConstraint));
            villageOnlyRule.Check(Architecture);
        }
    }
}