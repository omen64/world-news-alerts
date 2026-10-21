export interface User {
  id: string;
  email: string;
  displayName: string;
  emailConfirmed: boolean;
  isAdmin: boolean;
}
export interface Category {
  id: string;
  name: string;
}
export interface Catalog {
  categories: Category[];
  periods: Record<string, number>;
  channels: { key: string; enabled: boolean }[];
  newsProvider: string;
  developmentEmail: boolean;
  slackConfigured: boolean;
  publicSlackInviteUrl: string | null;
}
export interface Source {
  publisher: string;
  url: string;
  publishedAtUtc: string | null;
}
export interface News {
  id: string;
  title: string;
  summary: string;
  importance: string;
  location: string | null;
  eventTimeUtc: string | null;
  retrievedAtUtc: string;
  effectiveAtUtc: string;
  categories: string[];
  sources: Source[];
  revision: number;
  providerKey: string;
}
export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
export interface Destination {
  id: string;
  channelKey: string;
  kind: string;
  label: string;
  isActive: boolean;
  publicCategory: string | null;
}
export interface Subscription {
  id?: string;
  categories: string[];
  minimumImportance: string;
  emailEnabled: boolean;
  slackDestinationIds: string[];
  initialDigestLookbackHours: number;
  isActive: boolean;
  notifyUpdates: boolean;
}
export interface Delivery {
  id: string;
  subject: string;
  messageType: string;
  status: string;
  attemptCount: number;
  createdAtUtc: string;
  sentAtUtc: string | null;
  nextAttemptAtUtc: string;
  error: string | null;
  channel?: string;
}
export interface RuntimeSettings {
  newsProvider: string;
  retrievalEnabled: boolean;
  retrievalIntervalMinutes: number;
  lookbackMinutes: number;
  notificationsEnabled: boolean;
  emailEnabled: boolean;
  slackEnabled: boolean;
  emailProvider: string;
  pollingIntervalSeconds: number;
  maximumRetryCount: number;
  initialRetryDelaySeconds: number;
}
