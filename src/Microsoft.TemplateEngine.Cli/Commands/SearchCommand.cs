﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Cli.Extensions;
using Microsoft.TemplateEngine.Cli.TemplateSearch;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Microsoft.TemplateEngine.Cli.Commands
{
    internal class SearchCommand : BaseSearchCommand
    {
        public SearchCommand(
                NewCommand parentCommand,
                ITemplateEngineHost host,
                ITelemetryLogger logger,
                NewCommandCallbacks callbacks)
            : base(parentCommand, host, logger, callbacks, "search")
        {
            foreach (KeyValuePair<FilterOptionDefinition, Option> legacyOption in parentCommand.LegacyFilters)
            {
                if (SupportedFilters.Contains(legacyOption.Key))
                {
                    AddValidator(symbolResult => ValidateOptionUsageInParent(symbolResult, legacyOption.Value));
                }
            }
            AddValidator(symbolResult => ValidateOptionUsageInParent(symbolResult, parentCommand.ColumnsAllOption));
            AddValidator(symbolResult => ValidateOptionUsageInParent(symbolResult, parentCommand.ColumnsOption));
            AddValidator(symbolResult => ValidateArgumentUsageInParent(symbolResult, parentCommand.ShortNameArgument));
        }
    }

    internal class LegacySearchCommand : BaseSearchCommand
    {
        public LegacySearchCommand(NewCommand newCommand, ITemplateEngineHost host, ITelemetryLogger logger, NewCommandCallbacks callbacks)
            : base(newCommand, host, logger, callbacks, "--search")
        {
            this.IsHidden = true;
            AddValidator(ValidateParentCommandArgumentIsNotUsed);
        }

        public override Option<bool> ColumnsAllOption => ParentCommand.ColumnsAllOption;

        public override Option<IReadOnlyList<string>> ColumnsOption => ParentCommand.ColumnsOption;

        protected override Option GetFilterOption(FilterOptionDefinition def)
        {
            return ParentCommand.LegacyFilters[def];
        }

        private string? ValidateParentCommandArgumentIsNotUsed(CommandResult commandResult)
        {
            var nameArgumentResult = commandResult.Children.FirstOrDefault(symbol => symbol.Symbol == this.NameArgument);
            if (nameArgumentResult == null)
            {
                return null;
            }
            var newCommandArgument = commandResult.Parent?.Children.FirstOrDefault(symbol => symbol.Symbol == ParentCommand.ShortNameArgument) as ArgumentResult;
            if (newCommandArgument != null)
            {
                //Invalid command syntax: argument '{0}' should be used after '{1}'.
                return string.Format(LocalizableStrings.Commands_Validator_WrongArgumentPosition, newCommandArgument.Tokens[0].Value, commandResult.Symbol.Name);
            }
            return null;
        }
    }

    internal class BaseSearchCommand : BaseCommand<SearchCommandArgs>, IFilterableCommand, ITabularOutputCommand
    {
        internal static readonly IReadOnlyList<FilterOptionDefinition> SupportedFilters = new List<FilterOptionDefinition>()
        {
            FilterOptionDefinition.AuthorFilter,
            FilterOptionDefinition.BaselineFilter,
            FilterOptionDefinition.LanguageFilter,
            FilterOptionDefinition.TypeFilter,
            FilterOptionDefinition.TagFilter,
            FilterOptionDefinition.PackageFilter
        };

        internal BaseSearchCommand(NewCommand parentCommand, ITemplateEngineHost host, ITelemetryLogger logger, NewCommandCallbacks callbacks, string commandName) : base(host, logger, callbacks, commandName, LocalizableStrings.OptionDescriptionSearch)
        {
            ParentCommand = parentCommand;
            Filters = SetupFilterOptions(SupportedFilters);

            this.AddArgument(NameArgument);
            SetupTabularOutputOptions(this);
        }

        public virtual Option<bool> ColumnsAllOption { get; } = SharedOptionsFactory.CreateColumnsAllOption();

        public virtual Option<IReadOnlyList<string>> ColumnsOption { get; } = SharedOptionsFactory.CreateColumnsOption();

        public IReadOnlyDictionary<FilterOptionDefinition, Option> Filters { get; protected set; }

        internal Argument<string> NameArgument { get; } = new("name")
        {
            Description = "Name of the template to search for",
            Arity = new ArgumentArity(0, 1)
        };

        internal NewCommand ParentCommand { get; }

        protected override async Task<NewCommandStatus> ExecuteAsync(SearchCommandArgs args, IEngineEnvironmentSettings environmentSettings, InvocationContext context)
        {
            using TemplatePackageManager templatePackageManager = new TemplatePackageManager(environmentSettings);
            //TODO: we need to await, otherwise templatePackageManager will be disposed.
            return await CliTemplateSearchCoordinator.SearchForTemplateMatchesAsync(
                environmentSettings,
                templatePackageManager,
                args,
                environmentSettings.GetDefaultLanguage(),
                context.GetCancellationToken()).ConfigureAwait(false);
        }

        protected override SearchCommandArgs ParseContext(ParseResult parseResult)
        {
            return new SearchCommandArgs(this, parseResult);
        }
    }

    internal class SearchCommandArgs : BaseFilterableArgs, ITabularOutputArgs
    {
        internal SearchCommandArgs(BaseSearchCommand command, ParseResult parseResult) : base(command, parseResult)
        {
            string? nameCriteria = parseResult.GetValueForArgument(command.NameArgument);
            if (!string.IsNullOrWhiteSpace(nameCriteria))
            {
                SearchNameCriteria = nameCriteria;
            }
            // for legacy case new command argument is also accepted
            else if (command is LegacySearchCommand legacySearchCommand)
            {
                string? newCommandArgument = parseResult.GetValueForArgument(legacySearchCommand.ParentCommand.ShortNameArgument);
                if (!string.IsNullOrWhiteSpace(newCommandArgument))
                {
                    SearchNameCriteria = newCommandArgument;
                }
            }
            (DisplayAllColumns, ColumnsToDisplay) = ParseTabularOutputSettings(command, parseResult);

            if (AppliedFilters.Contains(FilterOptionDefinition.LanguageFilter))
            {
                Language = GetFilterValue(FilterOptionDefinition.LanguageFilter);
            }
        }

        public bool DisplayAllColumns { get; }

        public IReadOnlyList<string>? ColumnsToDisplay { get; }

        internal string? SearchNameCriteria { get; }

        internal string? Language { get; }
    }
}