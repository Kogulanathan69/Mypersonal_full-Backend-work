using Microsoft.Extensions.DependencyInjection.Extensions;
using BuildMind.API.Endpoints.PlanEditor;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.API.Endpoints.Planning;
using BuildMind.API.Endpoints.Survey;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Administration;
using BuildMind.API.Routing;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.API.Composition;

/// <summary>
/// Captain local-integration profile: a Development-only composition that registers implemented features whose
/// normal-host activation is still pending approval. Production activation is NOT approved.
/// </summary>
/// <remarks>
/// Guards (all fail closed at start-up):
/// <list type="bullet">
/// <item>off unless <see cref="EnabledKey"/> is true;</item>
/// <item>refused outside the Development environment;</item>
/// <item>refused unless M1 JWT authentication is active (which in turn requires the database);</item>
/// <item>every protected write uses the ratified project-row lock with an owner and authorization-version
/// re-check.</item>
/// </list>
/// The existing per-feature gates (for example <c>AddIsolatedRequirementsFeature</c>, which is Testing-only) are
/// unchanged and not used by this profile.
/// </remarks>
public static class CaptainLocalIntegration
{
    public const string EnabledKey = "CaptainIntegration:Enabled";
    public const string CandidateLifetimeKey = "CaptainIntegration:CandidateLifetimeMinutes";

    /// <returns>True when the profile was registered.</returns>
    public static bool AddCaptainLocalIntegration(this WebApplicationBuilder builder, bool authenticationEnabled)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Configuration.GetValue<bool>(EnabledKey))
            return false;

        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "The Captain local-integration profile is restricted to Development; production activation is not approved.");
        if (!authenticationEnabled)
            throw new InvalidOperationException(
                $"{EnabledKey} requires end-user authentication (Authentication:Enabled); protected features are never anonymous.");

        var services = builder.Services;
        services.AddCaptainIntegrationInfrastructure();

        // Requirements (M5 services, unchanged) over the protected repository adapter.
        services.AddScoped<RequirementRevisionReader>();
        services.AddScoped<ValidatedRequirementRevisionWriter>();
        services.AddScoped<IRequirementsApplicationService, RequirementsApplicationService>();
        services.AddScoped<IRequirementsSectionApplicationService, RequirementsSectionApplicationService>();
        services.AddSingleton<IApiEndpointModule, RequirementsEndpointModule>();
        services.AddSingleton<IApiEndpointModule, RequirementsSectionsEndpointModule>();

        // M2 survey upload and status (existing services and module).
        services.AddSurveyFeature();

        // Captain planning: land boundary, generation jobs, saving a generated candidate as a plan.
        services.AddScoped<PlanningAccess>();
        services.AddScoped<LandService>();
        services.AddScoped<GenerationService>();
        services.AddScoped<PlanSaveService>();
        // Evidence modes: none / synthetic-fixture (policy) / rag-constraints (existing M7 contract, fail closed).
        services.AddSingleton<PolicyRegulatoryEvidenceProvider>();
        services.AddScoped<IRegulatoryEvidenceProvider, RagRegulatoryEvidenceProvider>();
        services.AddSingleton<IApiEndpointModule, PlanningEndpointModule>();

        // M3 plan editor (existing services and module): revision history, edit candidates (create/apply/reject),
        // plus the Captain plan list/read and final confirmation routes. Time is truncated to microseconds because the
        // M3 services compare stored timestamps exactly and MySQL datetime(6) keeps microseconds.
        var candidateMinutes = builder.Configuration.GetValue<int?>(CandidateLifetimeKey) ?? 30;
        if (candidateMinutes is < 1 or > 24 * 60)
            throw new InvalidOperationException($"{CandidateLifetimeKey} must be between 1 and 1440 minutes.");
        services.AddScoped(provider => new CandidatePreviewService(
            provider.GetRequiredService<IProjectOwnershipGate>(), provider.GetRequiredService<IPlanRevisionStore>(),
            provider.GetRequiredService<IApplicableInputRevisionSource>(), provider.GetRequiredService<IConfirmedLandRevisionPort>(),
            provider.GetRequiredService<IAuthorizedRequirementRevisionPort>(), provider.GetRequiredService<IPlanEditorComputeClient>(),
            provider.GetRequiredService<ICandidateValidationEvidenceSource>(), provider.GetRequiredService<IPlanValidationEvidenceVerifier>(),
            provider.GetRequiredService<ICandidatePreviewStore>(), provider.GetRequiredService<ICandidatePreviewTransaction>(),
            new MicrosecondTimeProvider(provider.GetRequiredService<TimeProvider>()), TimeSpan.FromMinutes(candidateMinutes)));
        services.AddScoped<IEditValidationApplicationFacade, EditValidationApplicationFacade>();
        services.AddScoped<ITrustedCandidateActionFacade, TrustedCandidateActionFacade>();
        services.AddScoped(provider => new FinalPlanConfirmationService(
            provider.GetRequiredService<IProjectOwnershipGate>(), provider.GetRequiredService<IPlanRevisionStore>(),
            provider.GetRequiredService<IApplicableInputRevisionSource>(), provider.GetRequiredService<IValidationFreshnessPolicy>(),
            provider.GetRequiredService<IFinalConfirmationTransaction>(), provider.GetRequiredService<IConfirmedPlanSnapshotSink>(),
            new MicrosecondTimeProvider(provider.GetRequiredService<TimeProvider>())));
        services.AddSingleton<IApiEndpointModule, PlanEditorEndpointModule>();
        services.AddSingleton<IApiEndpointModule, PlanLifecycleEndpointModule>();

        // Phase 6: existing M5 report generation/read and architect workflow candidates over Captain boundaries.
        services.AddScoped(provider => new BuildMind.Application.Features.Reports.Services.ReportGenerationCandidate(
            provider.GetRequiredService<BuildMind.Application.Features.Reports.Services.IReportGenerationBoundaryCandidate>(),
            provider.GetRequiredService<BuildMind.Application.Features.Reports.Services.IPreliminaryReportRenderer>()));
        services.AddScoped(provider => new BuildMind.Application.Features.Reports.Services.ReportReadCandidate(
            provider.GetRequiredService<BuildMind.Application.Features.Reports.Services.IReportReadBoundaryCandidate>()));
        services.AddScoped(provider => new BuildMind.Application.Features.ArchitectRequests.Services.ArchitectWorkflowCandidate(
            provider.GetRequiredService<BuildMind.Application.Features.ArchitectRequests.Services.IArchitectWorkflowBoundaryCandidate>(),
            new MicrosecondTimeProvider(provider.GetRequiredService<TimeProvider>())));
        services.AddSingleton<IApiEndpointModule, DeliverablesEndpointModule>();

        // V3 register completion: extraction results, land validate/confirm, alternatives/select, create plan, edit
        // validation (no persistence), server-validated version save, confirm alias, AI proposals (no provider), reports list.
        services.AddScoped<PlanVersionService>();
        services.AddScoped<SurveyReviewService>();
        services.AddScoped<AiEditOrchestrationService>();
        services.AddSingleton<IApiEndpointModule, V3RouteCompletionEndpointModule>();

        // Internal mTLS to python-services (Python client only; the protected RAG client is unchanged, STEP 8B).
        if (InternalMtls.IsConfigured(builder.Configuration))
            services.AddHttpClient<BuildMind.Infrastructure.Integration.PythonServiceClient>()
                .ConfigurePrimaryHttpMessageHandler(provider => InternalMtls.CreatePythonHandler(provider.GetRequiredService<IConfiguration>()));
        // API -> rag-service over the same internal mTLS trust (server CA only, hostname check, client certificate) when
        // the RAG base address is https. A plain-http RAG address keeps the token-only transport (no silent downgrade:
        // an https address always gets this handler).
        if (InternalMtls.IsConfigured(builder.Configuration)
            && (builder.Configuration["Services:Rag:BaseUrl"] ?? "").StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<BuildMind.Infrastructure.Integration.RagServiceClient>()
                .ConfigurePrimaryHttpMessageHandler(provider => InternalMtls.CreatePythonHandler(provider.GetRequiredService<IConfiguration>()));

        // LLM provider transport (OpenAI-compatible; local llama.cpp in Development). No proxy, no redirects, no logging.
        services.AddHttpClient(OpenAiCompatibleEditProposalPort.ClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false, AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5),
                AutomaticDecompression = System.Net.DecompressionMethods.None,
            })
            .RemoveAllLoggers();
        if (builder.Configuration["CaptainIntegration:AiEdit:Provider"] == "openai-compatible")
            services.Replace(ServiceDescriptor.Scoped<BuildMind.Application.Features.PlanEditor.Integration.IAiEditProposalPort>(provider =>
                new RecordingAiEditProposalPort(
                    new OpenAiCompatibleEditProposalPort(provider.GetRequiredService<BuildMind.Application.Features.PlanEditor.Versioning.IPlanRevisionStore>(),
                        provider.GetRequiredService<IHttpClientFactory>().CreateClient(OpenAiCompatibleEditProposalPort.ClientName),
                        provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<ILogger<OpenAiCompatibleEditProposalPort>>()),
                    provider.GetRequiredService<AiProposalRejectionContext>())));

        // Admin and delegated staff (proposed Captain-local role model; privileges read fresh per request).
        services.AddScoped<StaffInvitationService>();
        services.AddScoped<AccountSecurityService>();
        services.AddSingleton<IApiEndpointModule, BuildMind.API.Endpoints.Administration.AccountSecurityEndpointModule>();
        services.AddSingleton<IApiEndpointModule, BuildMind.API.Endpoints.Administration.AdministrationEndpointModule>();
        services.AddHostedService<AdminBootstrapHostedService>();
        services.AddSingleton<IApiEndpointModule, BuildMind.API.Endpoints.Administration.PortalEndpointModule>();
        services.AddHostedService<OutboxHostedService>();
        // SMTP delivery (loopback-only unless explicitly allowed) replaces the non-delivering recovery sender.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["CaptainIntegration:Email:SmtpHost"]))
            services.UseCaptainSmtpForPasswordRecovery();

        // Durable survey extraction worker: only when the private Python service is configured (fail closed otherwise:
        // surveys stay Queued). Disable explicitly with CaptainIntegration:SurveyWorker:Enabled=false.
        if (builder.Configuration.GetValue<bool>("Services:Python:Enabled")
            && builder.Configuration.GetValue("CaptainIntegration:SurveyWorker:Enabled", true))
            services.AddHostedService<SurveyExtractionHostedService>();

        return true;
    }
}

/// <summary>Maps only the existing revision-history GET handlers of the M3 module.</summary>
public sealed class PlanRevisionHistoryEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        PlanEditorEndpointModule.MapRevisionHistoryEndpoints(apiV1);
    }
}
