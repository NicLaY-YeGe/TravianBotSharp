using MainCore.UI.Models.Input;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Validators
{
    public class VillageSettingInputValidator : AbstractValidator<VillageSettingInput>
    {
        public VillageSettingInputValidator()
        {
            RuleFor(x => x.Tribe.SelectedItem.Tribe)
                .NotEqual(TribeEnums.Any)
                .WithMessage("Tribe should be specific");

            RuleFor(x => x.TrainTroopRepeatTime.Min)
                .LessThanOrEqualTo(x => x.TrainTroopRepeatTime.Max)
                .WithMessage("Minimum next train troop ({PropertyValue}) should be less than maximum next train troop ({ComparisonValue})");
            RuleFor(x => x.TrainTroopRepeatTime.Min)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Minimum next train troop ({PropertyValue}) should be positive number");

            RuleFor(x => x.GreatBarrackAmount.Min)
                .LessThanOrEqualTo(x => x.GreatBarrackAmount.Max)
                .WithMessage("Minimum greatbarrack amount troop ({PropertyValue}) should be less than maximum greatbarrack amount troop ({ComparisonValue})");
            RuleFor(x => x.GreatBarrackAmount.Min)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Minimum greatbarrack amount troop ({PropertyValue}) should be positive number");

            RuleFor(x => x.GreatStableAmount.Min)
                .LessThanOrEqualTo(x => x.GreatStableAmount.Max)
                .WithMessage("Minimum greatstable amount troop ({PropertyValue}) should be less than maximum greatstable amount troop ({ComparisonValue})");
            RuleFor(x => x.GreatStableAmount.Min)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Minimum greatstable amount troop ({PropertyValue}) should be positive number");

            RuleFor(x => x.GreatBarrackRepeatTime.Min)
                .LessThanOrEqualTo(x => x.GreatBarrackRepeatTime.Max)
                .WithMessage("Minimum greatbarrack next visit ({PropertyValue}) should be less than maximum greatbarrack next visit ({ComparisonValue})");
            RuleFor(x => x.GreatBarrackRepeatTime.Min)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Minimum greatbarrack next visit ({PropertyValue}) should be positive number (0 = use the general time)");

            RuleFor(x => x.GreatStableRepeatTime.Min)
                .LessThanOrEqualTo(x => x.GreatStableRepeatTime.Max)
                .WithMessage("Minimum greatstable next visit ({PropertyValue}) should be less than maximum greatstable next visit ({ComparisonValue})");
            RuleFor(x => x.GreatStableRepeatTime.Min)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Minimum greatstable next visit ({PropertyValue}) should be positive number (0 = use the general time)");

            // 2026-10-27: per-troop rows (amount and interval).
            RuleFor(x => x).Custom((input, context) =>
            {
                var groups = new (string Name, TroopSlotsViewModel Slots)[]
                {
                    ("infantry", input.InfantrySlots),
                    ("cavalry", input.CavalrySlots),
                    ("siege", input.SiegeSlots),
                };
                foreach (var (name, slots) in groups)
                {
                    foreach (var row in slots.Items)
                    {
                        var (amountMin, amountMax) = row.Amount.Get();
                        var (timeMin, timeMax) = row.Time.Get();
                        if (amountMin < 0 || amountMax < 0 || amountMin > amountMax)
                            context.AddFailure($"{name} {row.Name}: minimum amount ({amountMin}) should be 0 or more and not above the maximum ({amountMax})");
                        if (timeMin < 0 || timeMax < 0 || timeMin > timeMax)
                            context.AddFailure($"{name} {row.Name}: minimum time ({timeMin}) should be 0 or more and not above the maximum ({timeMax}) (0 = use the general time)");
                    }
                }
            });
        }
    }
}