using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Infrastructure.Persistence.Repositories.PlanEditor;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Survey;
using BuildMind.Infrastructure.Survey;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Infrastructure adapters for the Captain local-integration profile. The API composition root calls this only
/// after the Development-only guard has passed. Production composition never calls it.
/// </summary>
public static class CaptainIntegrationInfrastructure
{
    public static IServiceCollection AddCaptainIntegrationInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Shared fresh owner-scoped authorization read (Option A, C-01 A1/A2/A8).
        services.TryAddScoped<IProjectAuthorizationPort, ProjectOwnerAuthorizationPort>();

        // M3 revision history read side (existing MySQL adapter).
        services.TryAddScoped<IPlanRevisionHistoryQueryPort, MySqlPlanRevisionHistoryQuery>();
        services.TryAddScoped<IPlanRevisionHistoryService, PlanRevisionHistoryService>();

        // Requirements: every write uses the protected atomic path; the policy comes from an operator-supplied file.
        services.TryAddScoped<RequirementRevisionRepository>();
        services.Replace(ServiceDescriptor.Scoped<IRequirementRevisionRepository, OwnerProtectedRequirementRevisionRepository>());
        services.TryAddSingleton<IRequirementCatalogueProvider, ConfiguredRequirementCatalogueProvider>();

        // M2 survey upload/status: protected insert, CAS state updates, operator-provisioned private blob directory.
        services.TryAddScoped<IProjectOwnershipGate, ProjectOwnershipGate>();
        services.TryAddScoped<ISurveyRepository, SurveyRepository>();
        services.TryAddSingleton<ISurveyStorage>(provider =>
        {
            var directory = provider.GetRequiredService<IConfiguration>()[SurveyStorageDirectoryKey];
            return new PrivateSurveyFileStorage(directory
                ?? throw new InvalidOperationException($"{SurveyStorageDirectoryKey} is required for survey upload."));
        });

        // Captain planning: land revisions, generation jobs/candidates, plan creation, Python compute routes.
        services.TryAddScoped<ILandRevisionStore, MySqlLandRevisionStore>();
        services.TryAddScoped<IGenerationJobStore, MySqlGenerationJobStore>();
        services.TryAddScoped<IPlanCreationStore, MySqlPlanCreationStore>();
        services.TryAddTransient<IPlanningComputeClient, PlanningComputeClient>();
        services.TryAddSingleton<IGenerationPolicyProvider, FileGenerationPolicyProvider>();

        // Phase 4: adapters for the existing M3 plan editor, revision and confirmation services.
        services.TryAddScoped<IPlanRevisionStore, MySqlPlanRevisionStore>();
        services.TryAddScoped<IApplicableInputRevisionSource, CaptainApplicableInputs>();
        services.TryAddScoped<IConfirmedLandRevisionPort, CaptainConfirmedLandPort>();
        services.TryAddScoped<IAuthorizedRequirementRevisionPort, CaptainAuthorizedRequirementPort>();
        services.TryAddScoped<EditGeometryHandoff>();
        services.TryAddScoped<IPlanEditorComputeClient, PlanEditorComputeClient>();
        services.TryAddScoped<ValidationLedgerWriter>();
        services.TryAddScoped<ICandidateValidationEvidenceSource, LedgerValidationEvidenceSource>();
        services.TryAddScoped<IPlanValidationEvidenceVerifier, LedgerValidationEvidenceVerifier>();
        services.TryAddScoped<MySqlCandidatePreviewStore>();
        services.TryAddScoped<ICandidatePreviewStore>(p => p.GetRequiredService<MySqlCandidatePreviewStore>());
        services.TryAddScoped<ICandidatePreviewTransaction>(p => p.GetRequiredService<MySqlCandidatePreviewStore>());
        services.TryAddSingleton<IValidationFreshnessPolicy, ConfiguredValidationFreshnessPolicy>();
        services.TryAddScoped<IFinalConfirmationTransaction, MySqlFinalConfirmationTransaction>();
        services.TryAddSingleton<IConfirmedPlanSnapshotSink, DurableConfirmationSink>();
        services.TryAddScoped<IPlanCatalog, MySqlPlanCatalog>();

        // Phase 6: reports (existing M5 generation/read candidates + PDF renderer) and architect workflow.
        services.TryAddSingleton<PrivateReportArtifactStore>();
        services.TryAddScoped<CaptainReportBoundary>();
        services.TryAddScoped<BuildMind.Application.Features.Reports.Services.IReportGenerationBoundaryCandidate>(p => p.GetRequiredService<CaptainReportBoundary>());
        services.TryAddScoped<BuildMind.Application.Features.Reports.Services.IReportReadBoundaryCandidate>(p => p.GetRequiredService<CaptainReportBoundary>());
        services.TryAddSingleton<BuildMind.Application.Features.Reports.Services.IPreliminaryReportRenderer>(_ => new BuildMind.Infrastructure.Reports.PreliminaryPdfRenderer());
        services.TryAddScoped<BuildMind.Application.Features.ArchitectRequests.Services.IArchitectWorkflowBoundaryCandidate, CaptainArchitectBoundary>();

        // Survey extraction: integrity-checked blob reads, durable lease-based queue, Python extraction client, reader.
        services.TryAddSingleton<SurveyBlobReader>();
        services.TryAddScoped<SurveyWorkQueue>();
        services.TryAddScoped<SurveyExtractionProcessor>();
        services.TryAddScoped<ISurveyExtractionWork, SurveyExtractionWork>();
        services.TryAddTransient<ISurveyExtractionClient, SurveyExtractionClient>();
        services.TryAddScoped<ISurveyExtractionReader, MySqlSurveyExtractionReader>();
        services.TryAddScoped<IPlanVersionAppendStore, MySqlPlanVersionAppendStore>();
        services.TryAddScoped<IReportCatalog, MySqlReportCatalog>();
        services.TryAddSingleton<IAiEditProposalPort, UnconfiguredAiEditProposalPort>();

        return services;
    }

    /// <summary>Absolute path of an operator-provisioned, exclusive private directory for survey files.</summary>
    public const string SurveyStorageDirectoryKey = "CaptainIntegration:SurveyStorageDirectory";
}
