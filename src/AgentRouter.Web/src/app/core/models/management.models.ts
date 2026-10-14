import type { components } from '../../../generated/management';

export type Configuration = components['schemas']['ManagementConfigurationResponse'];
export type Provider = components['schemas']['ManagementProviderResponse'];
export type Profile = components['schemas']['ManagementProfileResponse'];
export type Gateway = components['schemas']['GatewayLifecycleResponse'];
export type RequestSummary = components['schemas']['GatewayRequestSummary'];
export type ObservabilitySummary = components['schemas']['GatewayObservabilitySummary'];
export type BrowserSession = components['schemas']['BrowserSessionResponse'];
export type ProviderRequest = components['schemas']['WriteProviderRequest'];
export type ProfileRequest = components['schemas']['WriteProfileRequest'];
export type View = 'Overview' | 'Providers' | 'Routing' | 'Profiles' | 'Requests';
export type GatewayAction = 'start' | 'stop' | 'restart';
export const ROUTE_TIERS = ['Default', 'Opus', 'Sonnet', 'Haiku'] as const;
export type RouteTier = typeof ROUTE_TIERS[number];