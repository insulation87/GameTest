\set ON_ERROR_STOP on
begin;
insert into auth.users(id,email,email_confirmed_at) values
('00000000-0000-0000-0000-000000000001','first@example.com',now()),
('00000000-0000-0000-0000-000000000002','second@example.com',now()),
('00000000-0000-0000-0000-000000000003','outsider@example.com',now());
insert into public.game_profiles(id,code,name) values
('00000000-0000-0000-0000-000000000001','AAAAAAAAAAAA','First'),
('00000000-0000-0000-0000-000000000002','BBBBBBBBBBBB','Second'),
('00000000-0000-0000-0000-000000000003','CCCCCCCCCCCC','Outsider');
do $$ begin
  if has_function_privilege('anon','public.game_social(text,text,text)','execute') then raise exception 'Anonymous RPC exposed'; end if;
  if has_table_privilege('authenticated','public.game_profiles','select,insert,update,delete') or
     has_table_privilege('authenticated','public.game_friendships','select,insert,update,delete') then raise exception 'Direct table access exposed'; end if;
end $$;
set local role authenticated;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
do $$ declare state jsonb; begin
  state := public.game_social('request','BBBBBBBBBBBB');
  if jsonb_array_length(state->'outgoing') <> 1 or jsonb_array_length(state->'friends') <> 0 then raise exception 'Premature friendship'; end if;
  state := public.game_social('request','BBBBBBBBBBBB');
  if jsonb_array_length(state->'outgoing') <> 1 then raise exception 'Duplicate invitation'; end if;
  begin perform public.game_social('accept','00000000-0000-0000-0000-000000000002'); raise exception 'Sender accepted own invitation'; exception when invalid_parameter_value then null; end;
  begin perform public.game_social('request','AAAAAAAAAAAA'); raise exception 'Self invitation'; exception when invalid_parameter_value then null; end;
  begin perform public.game_social('profile',null,''); raise exception 'Empty nickname'; exception when invalid_parameter_value then null; end;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000003',true);
do $$ declare state jsonb; begin
  state := public.game_social('list');
  if jsonb_array_length(state->'incoming') <> 0 or jsonb_array_length(state->'outgoing') <> 0 or jsonb_array_length(state->'friends') <> 0 then raise exception 'Other player data leaked'; end if;
  begin perform public.game_social('accept','00000000-0000-0000-0000-000000000001'); raise exception 'Outsider accepted invite'; exception when invalid_parameter_value then null; end;
  perform public.game_social('remove','00000000-0000-0000-0000-000000000001');
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000002',true);
do $$ declare state jsonb; begin
  state := public.game_social('list');
  if jsonb_array_length(state->'incoming') <> 1 then raise exception 'Recipient lost invite'; end if;
  state := public.game_social('accept','00000000-0000-0000-0000-000000000001');
  if jsonb_array_length(state->'friends') <> 1 or jsonb_array_length(state->'incoming') <> 0 then raise exception 'Acceptance failed'; end if;
end $$;
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000001',true);
do $$ declare state jsonb; begin
  state := public.game_social('list');
  if jsonb_array_length(state->'friends') <> 1 then raise exception 'Mutual acceptance failed'; end if;
  state := public.game_social('remove','00000000-0000-0000-0000-000000000002');
  if jsonb_array_length(state->'friends') <> 0 then raise exception 'Removal failed'; end if;
end $$;
select set_config('request.jwt.claim.sub','',true);
do $$ begin
  begin perform public.game_social('list'); raise exception 'Unsigned user accepted'; exception when insufficient_privilege then null; end;
end $$;
rollback;
select 'PASS: anonymous/direct access denied; invites, consent, duplicates, outsider isolation, names and removal validated.';
