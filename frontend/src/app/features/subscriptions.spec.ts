import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { describe, it, expect, vi } from 'vitest';
import { Api } from '../core/api';
import { SubscriptionsPage } from './subscriptions';

describe('Subscription preferences', () => {
  it('loads stored preferences and saves the chosen categories and destinations', async () => {
    const subscription = {
      id: 's1',
      categories: ['health'],
      minimumImportance: 'exceptional',
      emailEnabled: true,
      slackDestinationIds: ['d1'],
      initialDigestLookbackHours: 72,
      isActive: true,
      notifyUpdates: false,
    };
    const api = {
      catalog: signal({
        categories: [
          { id: 'health', name: 'Health' },
          { id: 'geopolitics', name: 'Geopolitics' },
        ],
      }),
      user: signal({ email: 'reader@example.com' }),
      get: vi.fn(async (path: string) =>
        path === '/subscriptions'
          ? subscription
          : [
              {
                id: 'd1',
                channelKey: 'slack',
                kind: 'slack_dm',
                label: 'My messages',
                isActive: true,
              },
            ],
      ),
      send: vi.fn(async () => subscription),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [SubscriptionsPage],
      providers: [provideRouter([]), { provide: Api, useValue: api }],
    });
    const fixture = TestBed.createComponent(SubscriptionsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;
    await vi.waitFor(() => expect(component.selected()).toEqual(['health']));
    expect(component.form.getRawValue().initialDigestLookbackHours).toBe(72);
    component.toggleCategory('geopolitics');
    await component.save();
    expect(api.send).toHaveBeenCalledWith(
      'PUT',
      '/subscriptions',
      expect.objectContaining({
        categories: ['health', 'geopolitics'],
        slackDestinationIds: ['d1'],
        minimumImportance: 'exceptional',
      }),
    );
    expect(component.message()).toContain('Preferences saved');
  });
});
